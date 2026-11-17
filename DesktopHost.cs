using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Nova.Core;

namespace Nova.App.Services;

// Explorer's WorkerW arrangement is undocumented and differs between Windows builds.
public static class DesktopHost {
    const uint SpawnWorker = 0x052C;
    const int GWL_STYLE = -16, GWL_EXSTYLE = -20, WM_STYLECHANGING = 0x007C;
    const long WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_EX_LAYERED = 0x00080000;
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc f, IntPtr p);
    [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr child);
    [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", SetLastError=true)] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW", SetLastError=true)] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW", SetLastError=true)] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError=true)] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError=true)] static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern int MapWindowPoints(IntPtr from, IntPtr to, ref POINT pt, uint count);
    readonly record struct DesktopLayer(IntPtr Parent, IntPtr Icons, bool Raised);
    static string attachmentInfo = "not started";
    public static string LastAttachmentInfo => attachmentInfo;
    // WPF's HwndTarget normally strips WS_EX_LAYERED in WM_STYLECHANGING when
    // UsesPerPixelOpacity is false. Preserve it only for our wallpaper window,
    // without enabling per-pixel WPF transparency (which can hide HwndHost content).
    static void InstallLayeredStyleGuard(Window window, IntPtr hwnd) {
        if (HwndSource.FromHwnd(hwnd) is not HwndSource source) throw new InvalidOperationException("Не удалось подключить обработчик окна WPF.");
        HwndSourceHook hook = (IntPtr h, int message, IntPtr wParam, IntPtr lParam, ref bool handled) => {
            if (h == hwnd && message == WM_STYLECHANGING && wParam.ToInt64() == GWL_EXSTYLE && lParam != IntPtr.Zero) {
                // STYLESTRUCT contains two DWORDs; styleNew is the second one.
                int proposed = Marshal.ReadInt32(lParam, 4);
                Marshal.WriteInt32(lParam, 4, DesktopLayoutRules.PreserveLayeredStyle(proposed));
                handled = true; // prevent WPF HwndTarget from removing the bit again
            }
            return IntPtr.Zero;
        };
        source.AddHook(hook);
        window.Closed += (_, _) => { try { if (!source.IsDisposed) source.RemoveHook(hook); } catch (ObjectDisposedException) { } };
    }
    static void VerifyLayered(IntPtr hwnd) {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((ex & WS_EX_LAYERED) == 0) throw new InvalidOperationException($"Windows сняла признак WS_EX_LAYERED (стиль 0x{ex:X}).");
        if (!GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags)) throw new InvalidOperationException("Не удалось проверить параметры компоновки: код " + Marshal.GetLastWin32Error());
        attachmentInfo += $" exstyle=0x{ex:X} alpha={alpha} flags=0x{flags:X}";
        if (alpha != 255 || (flags & 2) == 0) throw new InvalidOperationException("Некорректные параметры компоновки окна обоев.");
    }
    static DesktopLayer Discover(IntPtr progman) {
        // Raised Explorer layout: icons and the wallpaper WorkerW are both children of Progman.
        IntPtr icons = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        IntPtr raisedWorker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (DesktopLayoutRules.Classify(icons != IntPtr.Zero, raisedWorker != IntPtr.Zero, false) == DesktopLayoutKind.Raised) return new DesktopLayer(progman, icons, true);
        // Classic Explorer layout: the WorkerW after the top-level window containing icons.
        IntPtr classicWorker = IntPtr.Zero, classicIcons = IntPtr.Zero;
        EnumWindows((h, _) => {
            var view = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero) return true;
            var next = FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
            if (next == IntPtr.Zero) return true;
            classicWorker = next; classicIcons = view; return false;
        }, IntPtr.Zero);
        return DesktopLayoutRules.Classify(icons != IntPtr.Zero, raisedWorker != IntPtr.Zero, classicWorker != IntPtr.Zero) == DesktopLayoutKind.Classic ? new DesktopLayer(classicWorker, classicIcons, false) : default;
    }
    public static async Task<IntPtr> AttachAsync(Window window, Forms.Screen screen) {
        attachmentInfo = "phase=discover";
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) throw new InvalidOperationException("Не найден рабочий стол Проводника Windows.");
        var layer = Discover(progman);
        if (layer.Parent == IntPtr.Zero) {
            // Ask Explorer for a wallpaper layer once, then let it finish creating windows.
            SendMessageTimeout(progman, SpawnWorker, new IntPtr(0xD), new IntPtr(1), 0x0002, 1000, out _);
            for (int i = 0; i < 12 && layer.Parent == IntPtr.Zero; i++) { await Task.Delay(100); layer = Discover(progman); }
        }
        if (layer.Parent == IntPtr.Zero) {
            SendMessageTimeout(progman, SpawnWorker, IntPtr.Zero, IntPtr.Zero, 0x0002, 1000, out _);
            for (int i = 0; i < 8 && layer.Parent == IntPtr.Zero; i++) { await Task.Delay(100); layer = Discover(progman); }
        }
        if (layer.Parent == IntPtr.Zero) throw new InvalidOperationException("Не удалось найти слой рабочего стола. Перезапустите Проводник и отключите другие приложения обоев; если ошибка повторится, сообщите версию Windows (Win+R → winver).");
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Окно обоев не создано.");
        attachmentInfo = "phase=" + (layer.Raised ? "raised" : "classic");
        if (layer.Raised) {
            // Keep WPF from discarding WS_EX_LAYERED during its style-change handler.
            InstallLayeredStyleGuard(window, hwnd);
            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            attachmentInfo += $" before=0x{ex:X}";
            Marshal.SetLastPInvokeError(0);
            var previous = SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_LAYERED));
            int styleError = Marshal.GetLastPInvokeError();
            long actual = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            attachmentInfo += $" setError={styleError} after=0x{actual:X}";
            if (previous == IntPtr.Zero && styleError != 0) throw new InvalidOperationException("Не удалось задать стиль окна обоев: код " + styleError);
            if ((actual & WS_EX_LAYERED) == 0) throw new InvalidOperationException("Windows не сохранила стиль слоя окна обоев.");
            if (!SetLayeredWindowAttributes(hwnd, 0, 255, 2)) throw new InvalidOperationException("Не удалось включить слой компоновки Windows: код " + Marshal.GetLastWin32Error());
            VerifyLayered(hwnd);
        }
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr((style | WS_CHILD) & ~WS_POPUP));
        SetParent(hwnd, layer.Parent);
        if (GetParent(hwnd) != layer.Parent) throw new InvalidOperationException("Не удалось разместить окно на рабочем столе: код " + Marshal.GetLastWin32Error());
        var r = screen.Bounds;
        var pt = new POINT { X = r.X, Y = r.Y };
        MapWindowPoints(IntPtr.Zero, layer.Parent, ref pt, 1);
        // Raised: insert immediately below the icon window. Classic: preserve z-order inside WorkerW.
        uint flags = 0x0010 | 0x0040 | (layer.Raised ? 0u : 0x0004u);
        if (!SetWindowPos(hwnd, layer.Raised ? layer.Icons : IntPtr.Zero, pt.X, pt.Y, r.Width, r.Height, flags)) throw new InvalidOperationException("Не удалось расположить обои на мониторе: код " + Marshal.GetLastWin32Error());
        if (layer.Raised) {
            VerifyLayered(hwnd);
            bool belowIcons = GetWindow(hwnd, 3) == layer.Icons; // GW_HWNDPREV
            attachmentInfo += $" immediatelyBelowIcons={belowIcons}";
            if (!belowIcons) throw new InvalidOperationException("Не удалось безопасно разместить обои ниже значков рабочего стола.");
        }
        attachmentInfo += $" placed={r.Width}x{r.Height} at {r.X},{r.Y}";
        return layer.Parent;
    }
    public static string Diagnostics() {
        var progman = FindWindow("Progman", null);
        var layer = progman == IntPtr.Zero ? default : Discover(progman);
        bool iconView = progman != IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
        bool childWorker = progman != IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "WorkerW", null) != IntPtr.Zero;
        return $"OS={RuntimeInformation.OSDescription}; x64={Environment.Is64BitOperatingSystem}; Progman={progman != IntPtr.Zero}; Progman.DefView={iconView}; Progman.WorkerW={childWorker}; layout={(layer.Parent == IntPtr.Zero ? "missing" : layer.Raised ? "raised" : "classic")}";
    }
    public static bool Alive(IntPtr worker) => worker != IntPtr.Zero && IsWindow(worker);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT rect);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder name, int max);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    public static bool IsFullscreen(Forms.Screen screen) {
        var h = GetForegroundWindow(); if (h == IntPtr.Zero || IsIconic(h) || !GetWindowRect(h, out var r)) return false;
        var b = new StringBuilder(128); GetClassName(h, b, b.Capacity);
        if (b.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        var s = screen.Bounds;
        return r.Left <= s.Left && r.Top <= s.Top && r.Right >= s.Right && r.Bottom >= s.Bottom;
    }
}

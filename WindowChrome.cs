using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace Nova.App.Services;
// Use the system title bar; request its dark variant where DWM supports it.
public static class WindowChrome {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public static void PreferDark(Window window) {
        try {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int enabled = 1;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
    }
}

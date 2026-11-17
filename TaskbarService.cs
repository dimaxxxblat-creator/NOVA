using System.Runtime.InteropServices;
using Nova.Core;
namespace Nova.App.Services;

// Experimental, undocumented API. Only the primary Explorer taskbar is touched.
// Original values are captured first; there is no change to the Windows registry.
public sealed class TaskbarService {
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy {
        public int State, Flags;
        public uint Color;
        public int Animation;
    }
    [StructLayout(LayoutKind.Sequential)] struct AttributeData {
        public int Attribute;
        public IntPtr Data;
        public UIntPtr Size;
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll", SetLastError=true)] static extern bool GetWindowCompositionAttribute(IntPtr hwnd, ref AttributeData data);
    [DllImport("user32.dll", SetLastError=true)] static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref AttributeData data);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    IntPtr appliedHandle;
    AccentPolicy? original, lastApplied;
    static bool Same(AccentPolicy a, AccentPolicy b) => a.State==b.State && a.Flags==b.Flags && a.Color==b.Color && a.Animation==b.Animation;
    static bool Access(IntPtr hwnd, ref AccentPolicy policy, bool write) {
        int size=Marshal.SizeOf<AccentPolicy>(); IntPtr p=Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(policy,p,false);
            var data=new AttributeData { Attribute=19, Data=p, Size=(UIntPtr)size };
            bool ok=write ? SetWindowCompositionAttribute(hwnd,ref data) : GetWindowCompositionAttribute(hwnd,ref data);
            if(ok && !write)policy=Marshal.PtrToStructure<AccentPolicy>(p);
            return ok;
        } finally { Marshal.FreeHGlobal(p); }
    }
    public void Apply(byte r, byte g, byte b, byte opacity) {
        var hwnd=FindWindow("Shell_TrayWnd",null);
        if(hwnd==IntPtr.Zero)throw new InvalidOperationException("Панель задач Проводника не найдена.");
        if(original.HasValue && hwnd!=appliedHandle) { original=null; lastApplied=null; }
        AccentPolicy current=new();
        if(!Access(hwnd,ref current,false)) throw new InvalidOperationException("Windows не разрешила прочитать текущий стиль панели. NOVA ничего не изменила; используйте настройки Windows.");
        if(original.HasValue && lastApplied.HasValue && !Same(current,lastApplied.Value)) throw new InvalidOperationException("Стиль панели изменился вне NOVA. Чтобы не перезаписать чужие изменения, новое оформление не применено. Нажмите «Завершить управление» и повторите действие.");
        var before=original ?? current;
        // ACCENT_ENABLE_TRANSPARENTGRADIENT, packed ABGR.
        var next=new AccentPolicy { State=2, Flags=2, Color=TaskbarColorFormat.PackAbgr(r,g,b,opacity) };
        if(!Access(hwnd,ref next,true)) throw new InvalidOperationException("Windows отклонила изменение оформления панели задач.");
        original=before; lastApplied=next; appliedHandle=hwnd;
    }
    // Do not overwrite changes made by Explorer or another application after NOVA.
    public string Restore() {
        if(!original.HasValue || !lastApplied.HasValue) return "NOVA сейчас не управляет панелью задач.";
        try {
            if(!IsWindow(appliedHandle) || FindWindow("Shell_TrayWnd",null)!=appliedHandle) return "Проводник перезапущен; исходная панель больше не существует.";
            AccentPolicy current=new();
            if(!Access(appliedHandle,ref current,false)) return "Не удалось проверить текущий стиль панели. Изменения не перезаписаны; откройте настройки Windows.";
            if(!Same(current,lastApplied.Value)) return "Стиль уже изменился вне NOVA. Он оставлен без изменений.";
            var snapshot=original.Value;
            return Access(appliedHandle,ref snapshot,true) ? "Отправлена команда восстановления прежнего стиля. Windows может обновить панель не сразу." : "Не удалось восстановить стиль автоматически. Измените оформление в настройках Windows.";
        } finally { original=null; lastApplied=null; appliedHandle=IntPtr.Zero; }
    }
}

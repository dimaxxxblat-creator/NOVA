using System.IO;
using Microsoft.Win32;
namespace Nova.App.Services;
public static class AutostartService {
    public static void Set(bool enabled) {
        using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) {
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Не найден путь к приложению.");
            if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Автозапуск доступен при запуске собранного NOVA.exe, а не через dotnet.");
            k.SetValue("NOVA", "\"" + exe + "\" --tray");
        } else k.DeleteValue("NOVA", false);
    }
}

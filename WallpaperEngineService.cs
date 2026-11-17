using System.IO;
using System.Diagnostics;
using Nova.Core;
namespace Nova.App.Services;
public static class WallpaperEngineService {
    public static string Detect() {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "wallpaper_engine", "wallpaper64.exe");
        return File.Exists(p) ? p : "";
    }
    public static bool Running => Process.GetProcessesByName("wallpaper32").Concat(Process.GetProcessesByName("wallpaper64")).Any();
    public static void Open(string exe, Wallpaper item, int monitor) {
        if (!File.Exists(exe) || !(Path.GetFileName(exe).Equals("wallpaper64.exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(exe).Equals("wallpaper32.exe", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Укажите wallpaper32.exe или wallpaper64.exe в настройках.");
        if (!Running) throw new InvalidOperationException("Сначала запустите Wallpaper Engine.");
        if (!File.Exists(item.Source)) throw new FileNotFoundException("Файл проекта не найден.");
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var s in new[] { "-control", "openWallpaper", "-file", item.Source, "-monitor", monitor.ToString(System.Globalization.CultureInfo.InvariantCulture) }) info.ArgumentList.Add(s);
        Process.Start(info)?.Dispose();
    }
}

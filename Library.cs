using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nova.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WallpaperKind { Video, Web, Scene, Application, Unsupported }
public sealed class Wallpaper : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public WallpaperKind Kind { get; set; }
    public string Source { get; set; } = ""; // project.json or direct entry
    public string Entry { get; set; } = "";
    string preview=""; bool favorite;
    public string Preview { get => preview; set { if(preview==value)return; preview=value; Changed(); } }
    public bool Favorite { get => favorite; set { if(favorite==value)return; favorite=value; Changed(); } }
    public bool Demo { get; set; }
    public string TypeLabel => Kind switch { WallpaperKind.Video => "Видео", WallpaperKind.Web => "Веб", WallpaperKind.Scene => "Сцена", WallpaperKind.Application => "Приложение", _ => "Не поддерживается" };
    [JsonIgnore] public string TypeIcon => Kind switch { WallpaperKind.Video => "▶", WallpaperKind.Web => "◈", WallpaperKind.Scene => "✦", WallpaperKind.Application => "⚙", _ => "?" };
    public string Availability => File.Exists(Source) && (Kind is WallpaperKind.Scene or WallpaperKind.Application || File.Exists(Entry)) ? "Доступно" : "Файл не найден";
    public string Compatibility => Kind switch { WallpaperKind.Video => "Встроенный видеодвижок VLC; поддержка зависит от кодека.", WallpaperKind.Web => "WebView2; не все API Wallpaper Engine реализованы.", WallpaperKind.Scene => "Требуется установленный и запущенный Wallpaper Engine.", WallpaperKind.Application => "Исполняемые обои не запускаются из NOVA.", _ => "Формат проекта не поддерживается." };
}
public sealed class MonitorAssignment { public string Monitor { get; set; } = ""; public Guid WallpaperId { get; set; } }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LibraryLayout { LargeCards, CompactCards, List }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccentTheme { Orange, Blue, Purple, Mint }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SeasonEffect { Snow, Leaves, Rain, Petals }
public sealed class Settings {
    public List<Wallpaper> Library { get; set; } = new();
    public Guid? ActiveId { get; set; }
    public string Monitor { get; set; } = "";
    // null means an old settings file; an empty list means deliberately stopped.
    public List<MonitorAssignment>? MonitorAssignments { get; set; }
    public List<Guid> RecentWallpapers { get; set; } = new();
    public LibraryLayout LibraryLayout { get; set; } = LibraryLayout.LargeCards;
    public bool AccessibleMode { get; set; }
    public bool AutoStart { get; set; }
    public bool AnimationsEnabled { get; set; } = true;
    public bool CompactNavigation { get; set; }
    public AccentTheme AccentTheme { get; set; } = AccentTheme.Orange;
    public int AccentStrength { get; set; } = 100;
    public bool AutoThumbnails { get; set; }
    public bool SnowEnabled { get; set; } = true;
    public bool GarlandEnabled { get; set; } = true;
    public bool DesktopSnowEnabled { get; set; }
    public bool WindowSnowCaps { get; set; } = true;
    public SeasonEffect DesktopSeason { get; set; } = SeasonEffect.Snow;
    public int DesktopIntensity { get; set; } = 100;
    public int DesktopSpeed { get; set; } = 100;
    public bool PauseFullscreen { get; set; } = true;
    public bool PauseLocked { get; set; } = true;
    public bool AllowNetwork { get; set; }
    public int Volume { get; set; }
    public string WallpaperEnginePath { get; set; } = "";
    public int WallpaperEngineMonitor { get; set; }
}
public static class WorkspaceRules {
    public static void Migrate(Settings state) {
        state.RecentWallpapers ??= new();
        if (state.MonitorAssignments==null) state.MonitorAssignments = state.ActiveId.HasValue && !string.IsNullOrWhiteSpace(state.Monitor)
            ? new List<MonitorAssignment> { new() { Monitor=state.Monitor, WallpaperId=state.ActiveId.Value } } : new();
        state.MonitorAssignments = state.MonitorAssignments.Where(x=>x!=null && !string.IsNullOrWhiteSpace(x.Monitor) && x.WallpaperId!=Guid.Empty)
            .GroupBy(x=>x.Monitor,StringComparer.OrdinalIgnoreCase).Select(x=>x.Last()).ToList();
        state.RecentWallpapers=state.RecentWallpapers.Where(x=>x!=Guid.Empty).Distinct().Take(12).ToList();
    }
    public static void Assign(Settings state,string monitor,Guid id) { Migrate(state); state.MonitorAssignments!.RemoveAll(x=>string.Equals(x.Monitor,monitor,StringComparison.OrdinalIgnoreCase)); state.MonitorAssignments.Add(new(){Monitor=monitor,WallpaperId=id}); if(string.IsNullOrWhiteSpace(state.Monitor))state.Monitor=monitor; state.ActiveId=id; }
    public static void Unassign(Settings state,string monitor) { Migrate(state); state.MonitorAssignments!.RemoveAll(x=>string.Equals(x.Monitor,monitor,StringComparison.OrdinalIgnoreCase)); var last=state.MonitorAssignments.LastOrDefault(); state.ActiveId=last?.WallpaperId; }
    public static void Remember(Settings state,Guid id) { Migrate(state); state.RecentWallpapers.Remove(id); if(id!=Guid.Empty)state.RecentWallpapers.Insert(0,id); if(state.RecentWallpapers.Count>12)state.RecentWallpapers.RemoveRange(12,state.RecentWallpapers.Count-12); }
    public static void RemoveWallpaper(Settings state,Guid id) { Migrate(state); state.MonitorAssignments!.RemoveAll(x=>x.WallpaperId==id); state.RecentWallpapers.RemoveAll(x=>x==id); if(state.ActiveId==id)state.ActiveId=state.MonitorAssignments.LastOrDefault()?.WallpaperId; }
}
public static class LibraryFilter {
    public static bool Matches(Wallpaper w,string query,string type,bool favoritesOnly) =>
        w.Title.Contains(query,StringComparison.CurrentCultureIgnoreCase) && (type=="Все типы" || type==w.TypeLabel) && (!favoritesOnly || w.Favorite);
}
public static class ColorThemeRules { public static int ClampStrength(int value) => Math.Clamp(value,50,100); }
public static class ProjectReader {
    static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".gif" };
    public static string SafePath(string root, string value) {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value)) throw new InvalidDataException("Путь в project.json должен быть относительным.");
        string baseDir = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(baseDir, value));
        if (!full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Путь проекта выходит за пределы папки.");
        // Reject existing reparse points in every component, including the project root.
        string? check = full;
        while (check != null && check.Length >= baseDir.Length) {
            if ((File.Exists(check) || Directory.Exists(check)) && (File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Ссылки и точки повторной обработки в проекте не поддерживаются.");
            if (string.Equals(check, baseDir, StringComparison.OrdinalIgnoreCase)) break;
            check = Path.GetDirectoryName(check);
        }
        return full;
    }
    static string Field(JsonElement root, string name) => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";
    public static Wallpaper Read(string input) {
        var path = Path.GetFullPath(input);
        if (Directory.Exists(path)) {
            var project = Path.Combine(path, "project.json");
            path = File.Exists(project) ? project : Path.Combine(path, "index.html");
        }
        if (!File.Exists(path)) throw new FileNotFoundException("Файл не найден", path);
        if (!Path.GetFileName(path).Equals("project.json", StringComparison.OrdinalIgnoreCase)) {
            string ext = Path.GetExtension(path);
            var kind = Video.Contains(ext) ? WallpaperKind.Video : ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase) ? WallpaperKind.Web : WallpaperKind.Unsupported;
            if (kind == WallpaperKind.Unsupported) throw new InvalidDataException("Выберите MP4, WebM, GIF, HTML или project.json.");
            return new Wallpaper { Title = Path.GetFileNameWithoutExtension(path), Kind = kind, Source = path, Entry = path };
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var json = doc.RootElement;
        if (json.ValueKind != JsonValueKind.Object) throw new InvalidDataException("project.json должен содержать объект.");
        var root = Path.GetDirectoryName(path)!;
        string type = Field(json, "type").ToLowerInvariant();
        string entry = Field(json, "file");
        var kind2 = type switch { "video" => WallpaperKind.Video, "web" => WallpaperKind.Web, "scene" => WallpaperKind.Scene, "application" => WallpaperKind.Application, _ => WallpaperKind.Unsupported };
        if (kind2 == WallpaperKind.Unsupported && (File.Exists(Path.Combine(root, "scene.pkg")) || entry.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))) kind2 = WallpaperKind.Scene;
        var safeEntry = string.IsNullOrWhiteSpace(entry) ? "" : SafePath(root, entry);
        if (kind2 == WallpaperKind.Video && !Video.Contains(Path.GetExtension(safeEntry))) kind2 = WallpaperKind.Unsupported;
        if (kind2 == WallpaperKind.Web && !new[] {".html", ".htm"}.Contains(Path.GetExtension(safeEntry), StringComparer.OrdinalIgnoreCase)) kind2 = WallpaperKind.Unsupported;
        string preview = Field(json, "preview");
        if (!string.IsNullOrEmpty(preview)) preview = SafePath(root, preview);
        string title = Field(json, "title");
        return new Wallpaper { Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(root) : title, Kind = kind2, Source = path, Entry = safeEntry, Preview = preview };
    }
}
public sealed class Storage {
    public string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVA");
    public string FilePath => Path.Combine(DirectoryPath, "state.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public Settings Load() {
        if (!File.Exists(FilePath)) return new();
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options) ?? new(); }
        catch (Exception e) when (e is JsonException or IOException) { throw new InvalidDataException("Не удалось прочитать настройки. Файл сохранён без изменений: " + FilePath, e); }
    }
    public void Save(Settings settings) {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, FilePath, true);
    }
}

public enum DesktopLayoutKind { Missing, Classic, Raised }
public static class DesktopLayoutRules {
    public static int PreserveLayeredStyle(int proposedStyle) => proposedStyle | 0x00080000;
    public static DesktopLayoutKind Classify(bool progmanIcons, bool progmanWorker, bool classicWorker) =>
        progmanIcons && progmanWorker ? DesktopLayoutKind.Raised : classicWorker ? DesktopLayoutKind.Classic : DesktopLayoutKind.Missing;
}
public static class TaskbarColorFormat {
    public static uint PackAbgr(byte red, byte green, byte blue, byte alpha) =>
        ((uint)alpha << 24) | ((uint)blue << 16) | ((uint)green << 8) | red;
}

public static class CursorBackupRules {
    public static bool MatchesApplied(string current, string applied) =>
        !string.IsNullOrWhiteSpace(applied) && string.Equals(current, applied, StringComparison.OrdinalIgnoreCase);
}

using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Nova.Core;

namespace Nova.App.Services;
/// <summary>Changes selected cursor roles and preserves originals in a durable rollback record.</summary>
public sealed class CursorService {
    const string KeyPath = @"Control Panel\Cursors";
    static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase) { "Arrow", "IBeam", "Wait", "AppStarting", "Hand" };
    readonly string backupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVA", "cursor-arrow-backup.json");
    sealed class Entry { public Entry() { } public bool Existed { get; set; } public string Original { get; set; } = ""; public int Kind { get; set; } public string Applied { get; set; } = ""; }
    sealed class Backup { public Backup() { } public int Version { get; set; } = 2; public Dictionary<string,Entry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase); }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadCursorFromFile(string path);
    [DllImport("user32.dll", SetLastError=true)] static extern bool DestroyCursor(IntPtr cursor);
    [DllImport("user32.dll", SetLastError=true)] static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint flags);
    static void Refresh() { if (!SystemParametersInfo(0x0057, 0, IntPtr.Zero, 0x0002)) throw new InvalidOperationException("Windows не обновила указатели: код " + Marshal.GetLastWin32Error()); }
    static void Validate(string path) {
        if (!File.Exists(path)) throw new FileNotFoundException("Файл курсора не найден.", path);
        if (!new[]{".cur", ".ani"}.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Выберите файлы .cur или .ani.");
        var h=LoadCursorFromFile(path); if(h==IntPtr.Zero)throw new InvalidDataException("Windows не смогла прочитать файл курсора: " + path + "; код " + Marshal.GetLastWin32Error()); DestroyCursor(h);
    }
    Backup? ReadBackup() {
        if(!File.Exists(backupPath))return null;
        string json=File.ReadAllText(backupPath); using var doc=JsonDocument.Parse(json);
        if(doc.RootElement.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("Резервная копия курсоров повреждена.");
        Backup result;
        if(doc.RootElement.TryGetProperty("Entries",out _)) result=JsonSerializer.Deserialize<Backup>(json) ?? throw new InvalidDataException("Резервная копия курсоров повреждена.");
        else { // v1 stored a single Arrow backup; keep the original instead of losing it.
            var old=JsonSerializer.Deserialize<Entry>(json) ?? throw new InvalidDataException("Старая резервная копия повреждена.");
            result=new Backup(); result.Entries.Add("Arrow",old);
        }
        if(result.Entries==null || result.Entries.Count==0 || result.Entries.Count>Supported.Count) throw new InvalidDataException("Резервная копия курсоров содержит некорректные записи.");
        var normalized=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in result.Entries) {
            if(!Supported.Contains(pair.Key) || pair.Value==null || string.IsNullOrWhiteSpace(pair.Value.Applied) || !normalized.TryAdd(pair.Key,pair.Value)) throw new InvalidDataException("Резервная копия курсоров содержит некорректную роль или путь.");
            if(pair.Value.Existed && (RegistryValueKind)pair.Value.Kind is not RegistryValueKind.String and not RegistryValueKind.ExpandString) throw new InvalidDataException("Резервная копия содержит неподдерживаемый тип реестра.");
        }
        result.Entries=normalized; return result;
    }
    void WriteBackup(Backup b) { Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!); string temp=backupPath+".tmp"; File.WriteAllText(temp,JsonSerializer.Serialize(b)); File.Move(temp,backupPath,true); }
    static Entry Current(RegistryKey key,string role) {
        bool exists=key.GetValueNames().Contains(role,StringComparer.OrdinalIgnoreCase);
        object? value=key.GetValue(role,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
        var kind=exists?key.GetValueKind(role):RegistryValueKind.String;
        if(exists && (value is not string || (kind!=RegistryValueKind.String && kind!=RegistryValueKind.ExpandString))) throw new InvalidOperationException("Текущий указатель " + role + " имеет неподдерживаемый тип в реестре. Изменений нет.");
        return new Entry { Existed=exists, Original=(value as string)??"", Kind=(int)kind };
    }
    static void WriteOriginal(RegistryKey key,string role,Entry value) { if(value.Existed)key.SetValue(role,value.Original,(RegistryValueKind)value.Kind); else key.DeleteValue(role,false); }
    static void CheckCurrent(RegistryKey key, Backup backup) {
        foreach(var pair in backup.Entries) {
            var cur=Current(key,pair.Key);
            if(!cur.Existed || !CursorBackupRules.MatchesApplied(cur.Original,pair.Value.Applied)) throw new InvalidOperationException("Указатель " + pair.Key + " изменён вне NOVA. Для защиты чужого выбора изменения отменены. Используйте настройки мыши Windows; резервная копия сохранена.");
        }
    }
    public string ApplySet(IReadOnlyDictionary<string,string> selection) {
        if(selection.Count==0)throw new InvalidOperationException("Добавьте хотя бы один файл курсора.");
        var chosen=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in selection) { if(!Supported.Contains(pair.Key))throw new InvalidDataException("Неизвестная роль курсора."); string path=Path.GetFullPath(pair.Value); Validate(path); if(!chosen.TryAdd(pair.Key,path))throw new InvalidDataException("Роль курсора повторяется."); }
        using var key=Registry.CurrentUser.CreateSubKey(KeyPath) ?? throw new InvalidOperationException("Не удалось открыть настройки указателей Windows.");
        var old=ReadBackup(); if(old!=null)CheckCurrent(key,old);
        var before=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        foreach(var role in chosen.Keys) before[role]=Current(key,role);
        var next=new Backup();
        if(old!=null)foreach(var p in old.Entries)next.Entries.Add(p.Key,new Entry {Existed=p.Value.Existed,Original=p.Value.Original,Kind=p.Value.Kind,Applied=p.Value.Applied});
        foreach(var p in chosen) { if(!next.Entries.TryGetValue(p.Key,out var entry)) { entry=before[p.Key]; next.Entries[p.Key]=entry; } entry.Applied=p.Value; }
        WriteBackup(next); // All original values are committed before the first registry write.
        try { foreach(var p in chosen) key.SetValue(p.Key,p.Value,RegistryValueKind.String); Refresh(); }
        catch(Exception ex) {
            bool rolledBack=false;
            try { foreach(var p in before)WriteOriginal(key,p.Key,p.Value); Refresh(); if(old==null)File.Delete(backupPath);else WriteBackup(old); rolledBack=true; } catch { }
            throw new InvalidOperationException(rolledBack ? "Применение не удалось; прежние значения возвращены: " + ex.Message : "Применение и автоматический откат не завершились. Резервная копия сохранена; проверьте указатели в настройках Windows. " + ex.Message,ex);
        }
        return $"Применено ролей: {chosen.Count}. Остальные указатели не изменены.";
    }
    public string Restore() {
        var backup=ReadBackup(); if(backup==null)return "Резервная копия отсутствует: NOVA не меняла указатели.";
        using var key=Registry.CurrentUser.CreateSubKey(KeyPath) ?? throw new InvalidOperationException("Не удалось открыть настройки курсоров.");
        CheckCurrent(key,backup);
        try { foreach(var p in backup.Entries)WriteOriginal(key,p.Key,p.Value); Refresh(); }
        catch(Exception ex) {
            try { foreach(var p in backup.Entries)key.SetValue(p.Key,p.Value.Applied,RegistryValueKind.String); Refresh(); } catch { }
            throw new InvalidOperationException("Не удалось восстановить все указатели. Резервная копия сохранена; проверьте настройки мыши Windows. " + ex.Message,ex);
        }
        File.Delete(backupPath); return "Сохранённые указатели восстановлены.";
    }
}

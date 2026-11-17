using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Nova.Core;
namespace Nova.App;
public sealed class ViewModel : INotifyPropertyChanged {
    public const string AppName = "NOVA";
    public ObservableCollection<Wallpaper> Library { get; } = new();
    string status="Остановлены", description="Обои ещё не установлены", page="Обои";
    public string Status { get => status; set { status=value; Changed(); } }
    public string Description { get => description; set { description=value; Changed(); } }
    public string Page { get => page; set { page=value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name=null) => PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
}

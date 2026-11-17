using System.Windows;
namespace Nova.App.Views;
public partial class ImportDialog : Window {
    public bool ChooseFolder { get; private set; }
    public ImportDialog() { InitializeComponent(); SourceInitialized += (_,_) => Nova.App.Services.WindowChrome.PreferDark(this); }
    void File_Click(object sender, RoutedEventArgs e) { ChooseFolder=false; DialogResult=true; }
    void Folder_Click(object sender, RoutedEventArgs e) { ChooseFolder=true; DialogResult=true; }
}

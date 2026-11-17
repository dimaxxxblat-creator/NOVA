using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Input;
using Nova.App.Views;
using System.Windows.Threading;
using Microsoft.Win32;
using Nova.App.Services;
using Nova.Core;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace Nova.App;
public partial class MainWindow : Window {
    readonly ViewModel vm = new(); readonly Storage storage = new(); readonly TaskbarService taskbar = new(); readonly DesktopSnowService desktopSnow = new(); readonly CursorService cursors = new(); readonly ThumbnailService thumbnails = new(); System.Windows.Input.Cursor? cursorPreview; readonly Dictionary<string,string> chosenCursors = new(StringComparer.OrdinalIgnoreCase); bool weatherReady, appearanceReady; Settings state = new();
    sealed class MonitorSession { public required WallpaperWindow Window; public bool UserPaused,AutoPaused,Ready; public string? Error; }
    readonly Dictionary<string,MonitorSession> hosts=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> monitorErrors=new(StringComparer.OrdinalIgnoreCase);
    bool locked, exiting, stateSafe=true, syncingMonitors; Forms.NotifyIcon? tray; System.Drawing.Icon? trayIcon; string currentPreview="\0", recentSignature="";
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    Func<Task>? noticeRetry;
    readonly Queue<Wallpaper> thumbnailQueue=new(); readonly HashSet<Guid> queuedThumbnails=new(); bool thumbnailQueueRunning; Task<byte[]>? thumbnailWork;
    sealed record MonitorItem(string Device, string Label);
    public MainWindow() {
        InitializeComponent(); SourceInitialized += (_,_) => WindowChrome.PreferDark(this); Title=ViewModel.AppName; DataContext=vm;
        try { state=storage.Load(); WorkspaceRules.Migrate(state); } catch (Exception e) { stateSafe=false; MessageBox.Show(e.Message + "\nСохранение отключено, чтобы не перезаписать данные.","Ошибка данных",MessageBoxButton.OK,MessageBoxImage.Error); WorkspaceRules.Migrate(state); }
        var demo=Path.Combine(AppContext.BaseDirectory,"Demo","index.html");
        var demoPreview=Path.Combine(AppContext.BaseDirectory,"Demo","preview.png");
        foreach (var w in state.Library) { if(w.Demo && File.Exists(demo)) { w.Source=demo; w.Entry=demo; w.Preview=demoPreview; } vm.Library.Add(w); }
        if (File.Exists(demo) && !vm.Library.Any(x=>x.Demo)) { var item=ProjectReader.Read(demo); item.Title="Оранжевая волна · Демо"; item.Demo=true; item.Preview=demoPreview; vm.Library.Insert(0,item); }
        if (string.IsNullOrEmpty(state.WallpaperEnginePath)) state.WallpaperEnginePath=WallpaperEngineService.Detect();
        state.AccentTheme=Enum.IsDefined(state.AccentTheme)?state.AccentTheme:AccentTheme.Orange; state.AccentStrength=ColorThemeRules.ClampStrength(state.AccentStrength); ThemeService.Apply(state.AccentTheme,state.AccentStrength); ThemeBox.SelectedIndex=(int)state.AccentTheme; AccentStrengthSlider.Value=state.AccentStrength; AutoThumbnailBox.IsChecked=state.AutoThumbnails;
        if(!Enum.IsDefined(state.LibraryLayout))state.LibraryLayout=LibraryLayout.LargeCards; LibraryViewBox.SelectedIndex=(int)state.LibraryLayout; ApplyLibraryLayout(); AccessibleModeBox.IsChecked=state.AccessibleMode; App.AnimationsEnabled=state.AnimationsEnabled && !state.AccessibleMode; ShowAnimations.IsChecked=state.AnimationsEnabled; appearanceReady=true; ApplyNavigation(); noticeTimer.Tick+=(_,_)=> { noticeTimer.Stop(); Notice.Visibility=Visibility.Collapsed; };
        AutoStart.IsChecked=state.AutoStart; ShowSnow.IsChecked=state.SnowEnabled; SnowLayer.IsSnowing=state.SnowEnabled; ShowGarland.IsChecked=state.GarlandEnabled; GarlandLayer.IsLit=state.GarlandEnabled; PauseFullscreen.IsChecked=state.PauseFullscreen; PauseLocked.IsChecked=state.PauseLocked; AllowNetwork.IsChecked=state.AllowNetwork; Volume.Value=state.Volume; WEPath.Text=state.WallpaperEnginePath; WEIndex.Text=state.WallpaperEngineMonitor.ToString(); DataPath.Text="Данные: " + storage.DirectoryPath;
        state.DesktopSeason=Enum.IsDefined(state.DesktopSeason)?state.DesktopSeason:SeasonEffect.Snow; state.DesktopIntensity=Math.Clamp(state.DesktopIntensity,25,200); state.DesktopSpeed=Math.Clamp(state.DesktopSpeed,25,200);
        SeasonBox.SelectedIndex=(int)state.DesktopSeason; SeasonIntensity.Value=state.DesktopIntensity; SeasonSpeed.Value=state.DesktopSpeed;
        ShowDesktopCaps.IsChecked=state.WindowSnowCaps; desktopSnow.CapsEnabled=state.WindowSnowCaps; desktopSnow.Configure(state.DesktopSeason,state.DesktopIntensity,state.DesktopSpeed); SeasonPreview.Configure(state.DesktopSeason,state.DesktopIntensity,state.DesktopSpeed); weatherReady=true; ApplyAccessibility(false); desktopSnow.Error += msg => { state.DesktopSnowEnabled=false; DesktopSnowStatus.Text=msg; DesktopSnowButton.Content="Включить эффект"; Save(); ShowNotice("Эффект остановлен",msg,true,()=>{ ToggleDesktopSnow_Click(this,new RoutedEventArgs()); return Task.CompletedTask; }); }; RefreshMonitors(); Filter_Changed(this, null!); ShowPage("Обои"); SetupTray();
        Microsoft.Win32.SystemEvents.SessionSwitch += SessionSwitch; Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        timer.Tick += TimerTick; timer.Start();
        Loaded += async (_,_) => { if (Environment.GetCommandLineArgs().Contains("--tray")) Hide(); QueueThumbnails(vm.Library); if(state.DesktopSnowEnabled && !state.AccessibleMode) { try { desktopSnow.Start(); DesktopSnowStatus.Text="Эффект включён."; DesktopSnowButton.Content="Выключить эффект"; } catch(Exception ex) { state.DesktopSnowEnabled=false; DesktopSnowStatus.Text="Не удалось включить эффект: " + ex.Message; Save(); } } await RestoreAsync(); };
    }
    void SetupTray() {
        try { trayIcon=TrayAppearance.MakeIcon(state.AccentTheme); } catch { trayIcon=(System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone(); } tray=new Forms.NotifyIcon { Icon=trayIcon, Text=ViewModel.AppName, Visible=true };
        var menu=new Forms.ContextMenuStrip { Renderer=TrayAppearance.Renderer(), BackColor=System.Drawing.Color.FromArgb(30,30,34), ForeColor=System.Drawing.Color.WhiteSmoke, ShowImageMargin=false, Padding=new Forms.Padding(6) };
        menu.Opening+=(_,_)=>BuildTrayMenu(menu); tray.ContextMenuStrip=menu; tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(OpenWindow);
    }
    void UpdateTrayIcon() { if(tray==null)return; try { var next=TrayAppearance.MakeIcon(state.AccentTheme); var old=trayIcon; trayIcon=next; tray.Icon=next; old?.Dispose(); } catch { } }
    void BuildTrayMenu(Forms.ContextMenuStrip menu) {
        foreach(var item in menu.Items.Cast<Forms.ToolStripItem>().ToArray())item.Dispose(); menu.Items.Clear();
        Forms.ToolStripMenuItem Add(string text,Action click,bool enabled=true) { var item=new Forms.ToolStripMenuItem(text) { Enabled=enabled, ForeColor=System.Drawing.Color.WhiteSmoke, Padding=new Forms.Padding(9,6,9,6) }; item.Click+=(_,_)=>click(); menu.Items.Add(item); return item; }
        var header=new Forms.ToolStripMenuItem("NOVA  ·  УПРАВЛЕНИЕ") { Enabled=false, ForeColor=TrayAppearance.Accent(state.AccentTheme), Padding=new Forms.Padding(9,7,9,7) }; menu.Items.Add(header);
        Add("Открыть NOVA",OpenWindow);
        string? device=CurrentDevice; hosts.TryGetValue(device??"",out var active);
        var monitors=Add("Монитор: "+(device??"не выбран"),()=>{});
        foreach(var m in HomeMonitorBox.Items.Cast<MonitorItem>()) { var monitor=m; var child=new Forms.ToolStripMenuItem(m.Label) { Checked=m.Device==device, ForeColor=System.Drawing.Color.WhiteSmoke }; child.Click+=(_,_)=>{ HomeMonitorBox.SelectedItem=monitor; }; monitors.DropDownItems.Add(child); }
        Add(active?.UserPaused==true?"Продолжить обои":"Пауза обоев",()=>_ = TogglePause(),active?.Ready==true);
        Add("Остановить обои на мониторе",()=>Stop_Click(this,new RoutedEventArgs()),active!=null || (device!=null && state.MonitorAssignments?.Any(x=>string.Equals(x.Monitor,device,StringComparison.OrdinalIgnoreCase))==true));
        Add(state.AccessibleMode?"Эффекты отключены режимом доступности":desktopSnow.Running?"Выключить эффект":"Включить эффект",()=>ToggleDesktopSnow_Click(this,new RoutedEventArgs()),!state.AccessibleMode);
        var favorites=Add("Избранное",()=>{});
        var openFavorites=new Forms.ToolStripMenuItem("Открыть всё избранное") { ForeColor=System.Drawing.Color.WhiteSmoke }; openFavorites.Click+=(_,_)=> { OpenWindow(); Search.Text=""; Filter.SelectedIndex=0; FavoritesOnly.IsChecked=true; Filter_Changed(this,null!); ShowPage("Библиотека"); }; favorites.DropDownItems.Add(openFavorites);
        favorites.DropDownItems.Add(new Forms.ToolStripSeparator()); var favs=vm.Library.Where(x=>x.Favorite).Take(12).ToArray(); foreach(var w in favs)AddTrayWallpaper(favorites,w,device);
        if(favs.Length==0)favorites.DropDownItems.Add(new Forms.ToolStripMenuItem("Нет избранных обоев") { Enabled=false });
        var recent=Add("Недавние",()=>{}); foreach(var id in state.RecentWallpapers.Take(8)) { var w=vm.Library.FirstOrDefault(x=>x.Id==id); if(w!=null)AddTrayWallpaper(recent,w,device); }
        if(recent.DropDownItems.Count==0)recent.DropDownItems.Add(new Forms.ToolStripMenuItem("История пуста") { Enabled=false });
        menu.Items.Add(new Forms.ToolStripSeparator()); Add("Открыть библиотеку",()=>{OpenWindow(); ShowPage("Библиотека");}); Add("Выход",Exit);
    }
    void AddTrayWallpaper(Forms.ToolStripMenuItem parent,Wallpaper item,string? device) {
        var entry=new Forms.ToolStripMenuItem(item.Title) { Enabled=device!=null && item.Kind is WallpaperKind.Video or WallpaperKind.Web or WallpaperKind.Scene, ForeColor=System.Drawing.Color.WhiteSmoke };
        entry.Click+=async (_,_)=> { if(item.Kind==WallpaperKind.Scene)OpenScene(item); else if(device!=null)await ApplyAsync(item,device,false); }; parent.DropDownItems.Add(entry);
    }
    void OpenWindow() { Show(); WindowState=WindowState.Normal; Activate(); }
    void Exit() { try { var restore=taskbar.Restore(); if (restore.StartsWith("Не удалось")) MessageBox.Show(restore, "Панель задач"); } catch (Exception ex) { MessageBox.Show("Не удалось восстановить панель задач: " + ex.Message); } exiting=true; desktopSnow.Dispose(); cursorPreview?.Dispose(); timer.Stop(); noticeTimer.Stop(); Microsoft.Win32.SystemEvents.SessionSwitch-=SessionSwitch; Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged; CloseAllHosts(false); if (tray!=null) { tray.Visible=false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); } trayIcon?.Dispose(); Save(); Close(); System.Windows.Application.Current.Shutdown(); }
    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) { if (!exiting) { e.Cancel=true; Hide(); tray?.ShowBalloonTip(2500,ViewModel.AppName,"Приложение продолжает работу в трее. Для выхода используйте меню значка.",Forms.ToolTipIcon.Info); } }
    bool Save() { if (!stateSafe) return false; try { state.Library=vm.Library.ToList(); storage.Save(state); return true; } catch (Exception e) { MessageBox.Show("Не удалось сохранить данные: " + e.Message); return false; } }
    void ShowPage(string page) {
        bool changed=vm.Page!=page; vm.Page=page;
        HomePage.Visibility=page=="Обои"?Visibility.Visible:Visibility.Collapsed; LibraryPage.Visibility=page=="Библиотека"?Visibility.Visible:Visibility.Collapsed; TaskbarPage.Visibility=page=="Панель задач"?Visibility.Visible:Visibility.Collapsed; DesktopSnowPage.Visibility=page=="Снег"?Visibility.Visible:Visibility.Collapsed; CursorPage.Visibility=page=="Курсор"?Visibility.Visible:Visibility.Collapsed; SettingsPage.Visibility=page=="Настройки"?Visibility.Visible:Visibility.Collapsed;
        foreach(var button in new[]{NavHome,NavLibrary,NavTaskbar,NavDesktopSnow,NavCursor,NavSettings}) button.Tag=null;
        var chosen=page=="Обои"?NavHome:page=="Библиотека"?NavLibrary:page=="Панель задач"?NavTaskbar:page=="Снег"?NavDesktopSnow:page=="Курсор"?NavCursor:NavSettings; chosen.Tag="Active";
        var active=page=="Обои"?HomePage:page=="Библиотека"?LibraryPage:page=="Панель задач"?TaskbarPage:page=="Снег"?DesktopSnowPage:page=="Курсор"?CursorPage:SettingsPage;
        active.BeginAnimation(OpacityProperty,null); if(changed && App.AnimationsEnabled)active.BeginAnimation(OpacityProperty,new DoubleAnimation(0.45,1,TimeSpan.FromMilliseconds(200))); if(changed)WorkspaceScroll.ScrollToTop();
    }
    void ApplyNavigation() {
        bool compact=state.CompactNavigation; NavColumn.Width=new GridLength(compact?76:226); NavigationGrid.Margin=compact?new Thickness(10,24,10,22):new Thickness(20,24,20,22);
        foreach(var label in new[]{NavHomeLabel,NavLibraryLabel,NavTaskbarLabel,NavDesktopSnowLabel,NavCursorLabel,NavSettingsLabel,WorkspaceLabel,AppLabel})label.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        BrandText.Visibility=compact?Visibility.Collapsed:Visibility.Visible; NavStatus.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        NavToggle.Content=compact?"▶":"◀   Свернуть меню"; NavToggle.ToolTip=compact?"Развернуть меню":"Свернуть меню";
    }
    void NavToggle_Click(object s,RoutedEventArgs e) { state.CompactNavigation=!state.CompactNavigation; ApplyNavigation(); Save(); }
    void Animations_Changed(object s,RoutedEventArgs e) { if(!appearanceReady)return; state.AnimationsEnabled=ShowAnimations.IsChecked==true; App.AnimationsEnabled=state.AnimationsEnabled && !state.AccessibleMode; Save(); }
    static string LayoutTemplate(LibraryLayout layout) => layout switch { LibraryLayout.CompactCards=>"CompactCards", LibraryLayout.List=>"ListCards", _=>"LargeCards" };
    void ApplyLibraryLayout() { LibraryList.ItemTemplate=(DataTemplate)FindResource(LayoutTemplate(state.LibraryLayout)); LibraryList.ItemsPanel=(ItemsPanelTemplate)FindResource(state.LibraryLayout==LibraryLayout.List?"ListPanel":"CardPanel"); }
    void LibraryView_Changed(object s,SelectionChangedEventArgs e) { if(!appearanceReady)return; state.LibraryLayout=(LibraryLayout)Math.Clamp(LibraryViewBox.SelectedIndex,0,2); ApplyLibraryLayout(); Save(); }
    void AccessibleMode_Changed(object s,RoutedEventArgs e) { if(!appearanceReady)return; state.AccessibleMode=AccessibleModeBox.IsChecked==true; ApplyAccessibility(true); Save(); }
    void ApplyAccessibility(bool mayRestartEffect) {
        bool accessible=state.AccessibleMode; RootGrid.LayoutTransform=new System.Windows.Media.ScaleTransform(accessible?1.12:1,accessible?1.12:1);
        System.Windows.Application.Current.Resources["NovaHint"]=new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accessible?"#F0F0F2":"#A6A5AB")!);
        System.Windows.Application.Current.Resources["NovaEyebrow"]=new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accessible?"#EAEAF0":"#8F8D95")!);
        App.AnimationsEnabled=state.AnimationsEnabled && !accessible; ShowAnimations.IsEnabled=!accessible; ShowSnow.IsEnabled=!accessible; ShowGarland.IsEnabled=!accessible;
        SnowLayer.IsSnowing=!accessible && ShowSnow.IsChecked==true; GarlandLayer.IsLit=!accessible && ShowGarland.IsChecked==true; SeasonPreview.IsSnowing=!accessible;
        DesktopSnowButton.IsEnabled=!accessible;
        if(accessible) { if(desktopSnow.Running)desktopSnow.Stop(); DesktopSnowStatus.Text="Эффект приостановлен режимом доступности."; DesktopSnowButton.Content="Включить эффект"; }
        else if(mayRestartEffect && state.DesktopSnowEnabled && !desktopSnow.Running) { try { desktopSnow.Start(); DesktopSnowStatus.Text="Эффект включён."; DesktopSnowButton.Content="Выключить эффект"; } catch(Exception ex) { state.DesktopSnowEnabled=false; DesktopSnowStatus.Text="Не удалось возобновить эффект: "+ex.Message; ShowNotice("Эффект не возобновлён",ex.Message,true); } }
        else if(!state.DesktopSnowEnabled)DesktopSnowStatus.Text="Эффект выключен.";
        RefreshWorkspace();
    }
    void Theme_Changed(object s,SelectionChangedEventArgs e) { if(!appearanceReady)return; state.AccentTheme=(AccentTheme)Math.Clamp(ThemeBox.SelectedIndex,0,3); ThemeService.Apply(state.AccentTheme,state.AccentStrength); UpdateTrayIcon(); if(App.AnimationsEnabled)SettingsPage.BeginAnimation(OpacityProperty,new DoubleAnimation(0.82,1,TimeSpan.FromMilliseconds(200))); Save(); }
    void AccentStrength_Changed(object s,RoutedPropertyChangedEventArgs<double> e) { if(!appearanceReady)return; state.AccentStrength=ColorThemeRules.ClampStrength((int)AccentStrengthSlider.Value); ThemeService.Apply(state.AccentTheme,state.AccentStrength); Save(); }
    void AutoThumbnail_Changed(object s,RoutedEventArgs e) {
        if(!appearanceReady)return; bool enable=AutoThumbnailBox.IsChecked==true;
        if(enable && MessageBox.Show("Windows будет создавать обложки для локальных видео без изображения. Она может использовать установленные сторонние обработчики и кодеки. Исходные файлы не изменяются. Разрешить?", "Миниатюры видео",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) { appearanceReady=false; AutoThumbnailBox.IsChecked=false; appearanceReady=true; return; }
        state.AutoThumbnails=enable; Save(); if(enable)QueueThumbnails(vm.Library); else { thumbnailQueue.Clear(); queuedThumbnails.Clear(); }
    }
    void ToggleFavorite_Click(object sender,RoutedEventArgs e) {
        e.Handled=true; if(sender is not System.Windows.Controls.Button button || button.DataContext is not Wallpaper w)return;
        w.Favorite=!w.Favorite;
        if(w.Favorite && App.AnimationsEnabled) { var scale=new System.Windows.Media.ScaleTransform(1,1); button.RenderTransform=scale; button.RenderTransformOrigin=new System.Windows.Point(.5,.5); var pulse=new DoubleAnimation(1,1.25,TimeSpan.FromMilliseconds(130)){AutoReverse=true}; scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,pulse); scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,pulse); }
        Save(); if(FavoritesOnly.IsChecked==true)Filter_Changed(this,null!);
    }
    void FavoriteFilter_Changed(object s,RoutedEventArgs e) { if(FavoritesOnly!=null)Filter_Changed(this,null!); }
    bool NeedsThumbnail(Wallpaper w) => w.Kind==WallpaperKind.Video && (string.IsNullOrWhiteSpace(w.Preview)||!File.Exists(w.Preview));
    void QueueThumbnails(IEnumerable<Wallpaper> items) {
        if(!state.AutoThumbnails || !stateSafe || exiting)return;
        foreach(var w in items.ToArray()) if(NeedsThumbnail(w) && ThumbnailService.IsLocalVideo(w.Entry) && queuedThumbnails.Add(w.Id))thumbnailQueue.Enqueue(w);
        if(!thumbnailQueueRunning && thumbnailQueue.Count>0) _=ProcessThumbnailQueue();
    }
    async Task ProcessThumbnailQueue() {
        thumbnailQueueRunning=true;
        try { while(state.AutoThumbnails && !exiting && thumbnailQueue.Count>0) { var item=thumbnailQueue.Dequeue(); try { if(vm.Library.Any(x=>ReferenceEquals(x,item)) && NeedsThumbnail(item))await CreateThumbnail(item,false); } catch { } finally { queuedThumbnails.Remove(item.Id); } } }
        finally { thumbnailQueueRunning=false; }
    }
    async Task CreateThumbnail(Wallpaper item,bool interactive) {
        if(!NeedsThumbnail(item)) { if(interactive)ShowNotice("Миниатюра не создана","У выбранных обоев уже есть обложка."); return; }
        string source=item.Entry;
        if(interactive) { ThumbnailButton.IsEnabled=false; ThumbnailButton.Content="Создание…"; }
        try {
            while(thumbnailWork is { IsCompleted:false }) { var previous=thumbnailWork; if(await Task.WhenAny(previous,Task.Delay(TimeSpan.FromSeconds(20)))!=previous) { if(!interactive) { thumbnailQueue.Clear(); queuedThumbnails.Clear(); } throw new TimeoutException("Предыдущий обработчик Windows ещё работает. Попробуйте позже."); } }
            if(exiting || (!interactive && !state.AutoThumbnails) || !vm.Library.Any(x=>ReferenceEquals(x,item)) || item.Entry!=source || !NeedsThumbnail(item))return;
            var work=thumbnails.CreateAsync(source); thumbnailWork=work;
            if(await Task.WhenAny(work,Task.Delay(TimeSpan.FromSeconds(20)))!=work) { _=work.ContinueWith(t=>{ _=t.Exception; },TaskContinuationOptions.OnlyOnFaulted); if(!interactive) { thumbnailQueue.Clear(); queuedThumbnails.Clear(); } throw new TimeoutException("Windows слишком долго создаёт миниатюру. Другие задачи подождут завершения обработчика."); }
            var bytes=await work;
            if(exiting || !vm.Library.Any(x=>ReferenceEquals(x,item)) || item.Entry!=source || !NeedsThumbnail(item))return;
            var folder=Path.Combine(storage.DirectoryPath,"thumbnails"); Directory.CreateDirectory(folder); string path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");
            File.WriteAllBytes(path,bytes); item.Preview=path; if(ReferenceEquals(LibraryList.SelectedItem,item))ThumbnailButton.IsEnabled=false;
            if(hosts.Values.Any(x=>ReferenceEquals(x.Window.Item,item)) && CurrentDevice!=null && hosts.TryGetValue(CurrentDevice,out var shown) && ReferenceEquals(shown.Window.Item,item))SetPreview(path);
            bool saved=Save(); if(interactive)ShowNotice(saved?"Миниатюра создана":"Миниатюра создана, но не сохранена в библиотеке",saved?"Обложка сохранена в данных NOVA. Исходный файл не изменён.":"Проверьте доступ к папке данных NOVA.",!saved);
        } catch(Exception ex) { if(interactive)ShowNotice("Не удалось создать миниатюру", "Windows не смогла получить кадр. Проверьте файл и поддержку кодека. "+ex.Message,true); }
        finally { if(interactive && ThumbnailButton!=null) { ThumbnailButton.Content="Создать миниатюру видео"; var current=LibraryList.SelectedItem as Wallpaper; ThumbnailButton.IsEnabled=current!=null && NeedsThumbnail(current); } }
    }
    async void Thumbnail_Click(object s,RoutedEventArgs e) { if(LibraryList.SelectedItem is not Wallpaper w)return; if(MessageBox.Show("Windows запросит миниатюру видео у установленных системных или сторонних обработчиков. Исходный файл не изменится. Продолжить?", "Миниатюра видео",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return; await CreateThumbnail(w,true); }
    void CardHoverEnter(object s,System.Windows.Input.MouseEventArgs e) { if(App.AnimationsEnabled && s is UIElement item)item.BeginAnimation(OpacityProperty,new DoubleAnimation(0.86,TimeSpan.FromMilliseconds(130))); }
    void CardHoverLeave(object s,System.Windows.Input.MouseEventArgs e) { if(s is UIElement item) { if(App.AnimationsEnabled)item.BeginAnimation(OpacityProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(160))); else item.BeginAnimation(OpacityProperty,null); } }
    void ShowNotice(string title,string message,bool error=false,Func<Task>? retry=null,bool diagnostics=false) {
        noticeTimer.Stop(); noticeRetry=retry; NoticeTitle.Text=title; NoticeMessage.Text=message;
        NoticeRetry.Visibility=retry==null?Visibility.Collapsed:Visibility.Visible;
        NoticeDiagnostics.Visibility=diagnostics && File.Exists(Path.Combine(storage.DirectoryPath,"desktop-diagnostics.log"))?Visibility.Visible:Visibility.Collapsed;
        Notice.Visibility=Visibility.Visible;
        if(!error)noticeTimer.Start();
        if(!IsVisible && tray!=null) tray.ShowBalloonTip(4000,title,message,error?Forms.ToolTipIcon.Warning:Forms.ToolTipIcon.Info);
    }
    void NoticeClose_Click(object s,RoutedEventArgs e) { noticeTimer.Stop(); noticeRetry=null; Notice.Visibility=Visibility.Collapsed; }
    async void NoticeRetry_Click(object s,RoutedEventArgs e) { var retry=noticeRetry; NoticeClose_Click(s,e); if(retry!=null)await retry(); }
    void NoticeDiagnostics_Click(object s,RoutedEventArgs e) { try { var path=Path.Combine(storage.DirectoryPath,"desktop-diagnostics.log"); if(!File.Exists(path))throw new FileNotFoundException("Файл диагностики пока не создан."); Process.Start(new ProcessStartInfo(path){UseShellExecute=true}); } catch(Exception ex) { MessageBox.Show("Не удалось открыть диагностику: "+ex.Message); } }
    void Home_Click(object s,RoutedEventArgs e)=>ShowPage("Обои");
    void Library_Click(object s,RoutedEventArgs e)=>ShowPage("Библиотека");
    void Settings_Click(object s,RoutedEventArgs e)=>ShowPage("Настройки");
    void Taskbar_Click(object s,RoutedEventArgs e)=>ShowPage("Панель задач");
    void DesktopSnowPage_Click(object s,RoutedEventArgs e)=>ShowPage("Снег");
    void CursorPage_Click(object s,RoutedEventArgs e)=>ShowPage("Курсор");
    string? CurrentDevice => (HomeMonitorBox.SelectedItem as MonitorItem)?.Device ?? (MonitorBox.SelectedItem as MonitorItem)?.Device;
    void RefreshMonitors() {
        string? old=CurrentDevice ?? state.Monitor; var screens=Forms.Screen.AllScreens;
        var items=screens.Select((s,i)=>new MonitorItem(s.DeviceName,$"{i+1}  ·  {s.DeviceName}  ·  {s.Bounds.Width} × {s.Bounds.Height}" + (s.Primary?"  ·  основной":""))).ToList();
        syncingMonitors=true; HomeMonitorBox.ItemsSource=items; MonitorBox.ItemsSource=items; var selected=items.FirstOrDefault(x=>x.Device==old) ?? items.FirstOrDefault(); HomeMonitorBox.SelectedItem=selected; MonitorBox.SelectedItem=selected; syncingMonitors=false; RefreshWorkspace();
    }
    void Monitor_Changed(object s,SelectionChangedEventArgs e) {
        if(syncingMonitors || HomeMonitorBox==null || MonitorBox==null)return;
        var item=(s as System.Windows.Controls.ComboBox)?.SelectedItem as MonitorItem; if(item==null)return;
        syncingMonitors=true; HomeMonitorBox.SelectedItem=item; MonitorBox.SelectedItem=item; syncingMonitors=false;
        state.Monitor=item.Device; RefreshWorkspace(); if(appearanceReady)Save();
    }
    void DisplayChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(() => {
        if(hosts.Count>0) { CloseAllHosts(false); ShowNotice("Мониторы изменились","Окна обоев остановлены. Подключите нужные экраны и примените обои снова; назначения сохранены.",true); }
        RefreshMonitors();
    });
    void SessionSwitch(object? s,SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(() => { if (e.Reason==SessionSwitchReason.SessionLock) { locked=true; desktopSnow.SetLocked(true); } if(e.Reason==SessionSwitchReason.SessionUnlock) { locked=false; desktopSnow.SetLocked(false); } });
    async void TimerTick(object? s,EventArgs e) {
        foreach(var pair in hosts.ToArray()) {
            var session=pair.Value; if(!session.Ready)continue;
            if(!DesktopHost.Alive(session.Window.Worker)) { CloseMonitor(pair.Key,false); monitorErrors[pair.Key]="Слой рабочего стола исчез. Примените обои снова."; continue; }
            var screen=Forms.Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==pair.Key);
            if(screen==null) { CloseMonitor(pair.Key,false); monitorErrors[pair.Key]="Монитор отключён."; continue; }
            bool reason=(state.PauseLocked && locked)||(state.PauseFullscreen && DesktopHost.IsFullscreen(screen));
            if(reason!=session.AutoPaused) { session.AutoPaused=reason; await UpdatePause(pair.Key,session); }
        }
        RefreshWorkspace();
    }
    void RefreshWorkspace() {
        if(HomeMonitorBox==null || ActiveMonitor==null || MonitorSummary==null)return;
        string? device=CurrentDevice; var monitor=HomeMonitorBox.SelectedItem as MonitorItem;
        if(device!=null && hosts.TryGetValue(device,out var active)) {
            vm.Description=active.Window.Item.Title; SetPreview(active.Window.Item.Preview);
            vm.Status=active.Error!=null ? "Ошибка: "+active.Error : !active.Ready ? "Загрузка…" : active.UserPaused || active.AutoPaused ? (active.Window.Item.Kind==WallpaperKind.Web?"Частичная пауза · веб":"На паузе") : "Запуск запрошен · "+active.Window.Item.TypeLabel;
            PauseButton.IsEnabled=active.Ready; PauseButton.Content=active.UserPaused?"Продолжить":"Пауза"; StopButton.IsEnabled=true; StopButton.Content="Остановить";
        } else {
            bool assigned=device!=null && state.MonitorAssignments?.Any(x=>string.Equals(x.Monitor,device,StringComparison.OrdinalIgnoreCase))==true;
            vm.Description="Нет активных обоев на этом мониторе"; SetPreview("");
            vm.Status=device!=null && monitorErrors.TryGetValue(device,out var error)?"Ошибка: "+error:assigned?"Остановлены · назначение сохранено":"Остановлены";
            PauseButton.IsEnabled=false; PauseButton.Content="Пауза"; StopButton.IsEnabled=assigned; StopButton.Content=assigned?"Забыть назначение":"Остановить";
        }
        ActiveMonitor.Text=monitor?.Label ?? "Монитор не выбран";
        int ready=hosts.Values.Count(x=>x.Ready); MonitorSummary.Text=$"Активных: {ready} / {Forms.Screen.AllScreens.Length}";
        EffectSummary.Text=state.AccessibleMode && state.DesktopSnowEnabled?"Приостановлен режимом доступности":desktopSnow.Running?SeasonLabel(state.DesktopSeason):"Выключен";
        RefreshRecent();
    }
    static string SeasonLabel(SeasonEffect effect)=>effect switch { SeasonEffect.Leaves=>"Осенние листья",SeasonEffect.Rain=>"Дождь",SeasonEffect.Petals=>"Цветочные лепестки",_=>"Снег" };
    void RefreshRecent() {
        if(RecentPanel==null || RecentEmpty==null)return; string signature=string.Join("|",state.RecentWallpapers.Select(id=>{ var w=vm.Library.FirstOrDefault(x=>x.Id==id); return id+":"+w?.Title+":"+(w==null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(w)); })); if(signature==recentSignature)return; recentSignature=signature; RecentPanel.Children.Clear();
        foreach(var id in state.RecentWallpapers.Take(8)) { var w=vm.Library.FirstOrDefault(x=>x.Id==id); if(w==null)continue;
            var button=new System.Windows.Controls.Button { Content=w.Title, Tag=w, ToolTip="Выбрать в библиотеке: "+w.Title, MaxWidth=210, Margin=new Thickness(0,0,8,8) }; button.Click+=Recent_Click; RecentPanel.Children.Add(button);
        }
        RecentEmpty.Visibility=RecentPanel.Children.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
    void Recent_Click(object sender,RoutedEventArgs e) { if(sender is not System.Windows.Controls.Button b || b.Tag is not Wallpaper w)return; Search.Text=""; Filter.SelectedIndex=0; FavoritesOnly.IsChecked=false; Filter_Changed(this,null!); ShowPage("Библиотека"); LibraryList.SelectedItem=w; }
    void Filter_Changed(object? s, EventArgs e) { if (LibraryList==null || Filter==null || Search==null) return; var view=CollectionViewSource.GetDefaultView(LibraryList.ItemsSource); if(view==null)return; string query=Search.Text.Trim(); string type=(Filter.SelectedItem as ComboBoxItem)?.Content?.ToString()??"Все типы"; view.Filter=o=>o is Wallpaper w && LibraryFilter.Matches(w,query,type,FavoritesOnly?.IsChecked==true); EmptyBox.Visibility=view.IsEmpty?Visibility.Visible:Visibility.Collapsed; LibraryEmpty.Text=vm.Library.Count==0?"В библиотеке пока нет обоев. Добавьте файл или папку проекта.":"По вашему запросу ничего не найдено."; }
    void LibraryList_SelectionChanged(object s,SelectionChangedEventArgs e) { var w=LibraryList.SelectedItem as Wallpaper; Details.Visibility=w==null?Visibility.Collapsed:Visibility.Visible; if(w==null)return; DetailTitle.Text=w.Title+(w.Demo?"  ·  Демо":""); DetailType.Text=w.TypeLabel+"  ·  "+w.Availability; DetailCompatibility.Text=w.Compatibility; DetailPath.Text=w.Source; ApplyButton.IsEnabled=w.Kind is WallpaperKind.Video or WallpaperKind.Web or WallpaperKind.Scene; ApplyButton.Content=w.Kind==WallpaperKind.Scene?"Открыть в Wallpaper Engine":"Применить"; ThumbnailButton.IsEnabled=NeedsThumbnail(w); Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Details.BringIntoView())); }
    async void Add_Click(object s,RoutedEventArgs e) {
        var dialog=new Nova.App.Views.ImportDialog { Owner=this };
        if(dialog.ShowDialog()!=true) return;
        string? path=null;
        if(!dialog.ChooseFolder) { var d=new Microsoft.Win32.OpenFileDialog { Filter="Обои и проекты|*.mp4;*.webm;*.gif;*.html;*.htm;project.json|Все файлы|*.*" }; if(d.ShowDialog()==true)path=d.FileName; }
        else { var d=new OpenFolderDialog { Title="Выберите папку проекта, либо папку с множеством проектов для поиска" }; if(d.ShowDialog()==true)path=d.FolderName; }
        if(path==null)return;
        try {
            List<Wallpaper> list;
            if(Directory.Exists(path) && !File.Exists(Path.Combine(path,"project.json")) && !File.Exists(Path.Combine(path,"index.html"))) {
                if(MessageBox.Show("Найти project.json в подпапках выбранной папки? Большая папка может потребовать времени.","Поиск проектов",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                vm.Status="Поиск проектов…";
                var result=await Task.Run(()=>Scan(path)); list=result.items; vm.Status="Поиск завершён"; if(result.errors>0)MessageBox.Show($"Не удалось прочитать проектов: {result.errors}. Остальные добавлены.");
            } else list=new(){await Task.Run(()=>ProjectReader.Read(path))};
            int n=0; foreach(var w in list) if(!vm.Library.Any(x=>string.Equals(x.Source,w.Source,StringComparison.OrdinalIgnoreCase))) { vm.Library.Add(w); n++; }
            Save(); Filter_Changed(this,null!); QueueThumbnails(list); ShowPage("Библиотека"); ShowNotice("Импорт завершён",$"Добавлено: {n}. Уже существующие записи пропущены.");
        } catch(Exception ex) { RefreshWorkspace(); ShowNotice("Не удалось импортировать",ex.Message,true,()=>{ Add_Click(this,new RoutedEventArgs()); return Task.CompletedTask; }); }
    }
    static (List<Wallpaper> items,int errors) Scan(string root) { var result=new List<Wallpaper>(); int errors=0; var options=new EnumerationOptions { RecurseSubdirectories=true, IgnoreInaccessible=true, AttributesToSkip=FileAttributes.ReparsePoint, MaxRecursionDepth=16 }; foreach(var p in Directory.EnumerateFiles(root,"project.json",options).Take(10000)) { try { result.Add(ProjectReader.Read(p)); } catch { errors++; } } return (result,errors); }
    async void Apply_Click(object s,RoutedEventArgs e) { if(LibraryList.SelectedItem is not Wallpaper w)return; if(w.Kind==WallpaperKind.Scene) { OpenScene(w); return; } if(CurrentDevice is string device)await ApplyAsync(w,device,false); else ShowNotice("Монитор не выбран","Выберите монитор и повторите попытку.",true); }
    async Task ApplyAsync(Wallpaper w,string device,bool restoring) {
        if(w.Kind is not (WallpaperKind.Video or WallpaperKind.Web))return;
        if(w.Kind==WallpaperKind.Web && !restoring && MessageBox.Show("Веб-обои содержат JavaScript. Запускайте только доверенные проекты. Доступ к сети задаётся в настройках. Продолжить?", "Безопасность",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        if(WallpaperEngineService.Running) { if(restoring) { monitorErrors[device]="Восстановление отменено: работает Wallpaper Engine."; RefreshWorkspace(); return; } if(MessageBox.Show("Wallpaper Engine запущен и может конфликтовать с NOVA. Отключите конфликтующие обои вручную. Продолжить?", "Возможен конфликт",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return; }
        var screen=Forms.Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==device);
        if(screen==null) { monitorErrors[device]="Монитор не найден"; RefreshWorkspace(); ShowNotice("Монитор не найден", "Выберите доступный монитор и попробуйте снова.",true); return; }
        CloseMonitor(device,false); monitorErrors.Remove(device);
        MonitorSession? session=null;
        try {
            var window=new WallpaperWindow(w,screen); session=new MonitorSession { Window=window }; hosts[device]=session; RefreshWorkspace();
            window.PlaybackError+=msg=>{ if(!hosts.TryGetValue(device,out var current) || !ReferenceEquals(current,session))return; session.Error=msg; LogDesktop("playback error="+msg); RefreshWorkspace(); ShowNotice("Ошибка воспроизведения",msg,true,()=>RetryWallpaper(w,device),true); };
            await window.StartAsync(screen,state.AllowNetwork,state.Volume);
            if(!hosts.TryGetValue(device,out var current) || !ReferenceEquals(current,session))return;
            session.Ready=true;
            if(!restoring) { WorkspaceRules.Assign(state,device,w.Id); WorkspaceRules.Remember(state,w.Id); }
            LogDesktop("start requested; monitor="+device+"; kind=" + w.Kind); Save(); RefreshWorkspace();
            if(!restoring && session.Error==null)ShowNotice("Запуск запрошен",w.Title+" · "+device+". Проверьте рабочий стол.");
        } catch(Exception ex) {
            if(session!=null && (!hosts.TryGetValue(device,out var current) || !ReferenceEquals(current,session)))return;
            CloseMonitor(device,false); monitorErrors[device]=ex.Message; LogDesktop("monitor="+device+" error=" + ex.Message); RefreshWorkspace(); ShowNotice("Не удалось применить обои",ex.Message,true,()=>RetryWallpaper(w,device),true);
        }
    }
    async Task RetryWallpaper(Wallpaper item,string monitor) {
        if(!vm.Library.Any(x=>ReferenceEquals(x,item)) || CurrentDevice!=monitor) { ShowPage("Библиотека"); ShowNotice("Выбор изменился", "Выберите обои и монитор в библиотеке и примените их снова.",true); return; }
        await ApplyAsync(item,monitor,false);
    }
    void LogDesktop(string detail) { try { Directory.CreateDirectory(storage.DirectoryPath); File.AppendAllText(Path.Combine(storage.DirectoryPath,"desktop-diagnostics.log"), $"{DateTime.Now:O} {DesktopHost.Diagnostics()} {DesktopHost.LastAttachmentInfo} {detail}{Environment.NewLine}"); } catch { } }
    void OpenScene(Wallpaper w) {
        if(!int.TryParse(WEIndex.Text,out int index)||index<0) { MessageBox.Show("В настройках укажите неотрицательный индекс монитора Wallpaper Engine."); return; }
        if(!WallpaperEngineService.Running) { MessageBox.Show("Для сцены необходим установленный и запущенный Wallpaper Engine. Укажите путь в настройках и запустите его."); return; }
        if(MessageBox.Show($"Wallpaper Engine заменит обои на своём мониторе № {index}. Порядок мониторов может отличаться от Windows. Чтобы избежать конфликта, NOVA остановит свои обои на всех мониторах и удалит их сохранённые назначения. Продолжить?", "Внешняя программа",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try { CloseAllHosts(false); WallpaperEngineService.Open(state.WallpaperEnginePath,w,index); CloseAllHosts(true); WorkspaceRules.Remember(state,w.Id); Save(); RefreshWorkspace(); ShowNotice("Команда отправлена", "Wallpaper Engine не сообщает NOVA статус воспроизведения."); } catch(Exception ex) { ShowNotice("Ошибка Wallpaper Engine",ex.Message,true); }
    }
    async Task RestoreAsync() {
        WorkspaceRules.Migrate(state); var assignments=state.MonitorAssignments!.ToArray();
        foreach(var a in assignments) { if(exiting)break; if(hosts.ContainsKey(a.Monitor) || !state.MonitorAssignments!.Any(x=>string.Equals(x.Monitor,a.Monitor,StringComparison.OrdinalIgnoreCase) && x.WallpaperId==a.WallpaperId))continue; var item=vm.Library.FirstOrDefault(x=>x.Id==a.WallpaperId); if(item==null)continue; if(!Forms.Screen.AllScreens.Any(x=>x.DeviceName==a.Monitor)) { monitorErrors[a.Monitor]="Монитор отключён; назначение сохранено."; continue; } await ApplyAsync(item,a.Monitor,true); }
        RefreshWorkspace();
    }
    void CloseMonitor(string device,bool clear) { if(hosts.Remove(device,out var session)) { try { session.Window.Close(); } catch { } } if(clear)WorkspaceRules.Unassign(state,device); }
    void CloseAllHosts(bool clear) { foreach(var device in hosts.Keys.ToArray())CloseMonitor(device,false); if(clear) { WorkspaceRules.Migrate(state); state.MonitorAssignments!.Clear(); state.ActiveId=null; } RefreshWorkspace(); }
    void Stop_Click(object s,RoutedEventArgs e) { if(CurrentDevice is not string device)return; CloseMonitor(device,true); monitorErrors.Remove(device); Save(); RefreshWorkspace(); ShowNotice("Обои остановлены", "Окно обоев NOVA на выбранном мониторе закрыто."); }
    async void Pause_Click(object s,RoutedEventArgs e)=>await TogglePause();
    async Task TogglePause() { if(CurrentDevice is not string device || !hosts.TryGetValue(device,out var session) || !session.Ready)return; session.UserPaused=!session.UserPaused; await UpdatePause(device,session); }
    async Task UpdatePause(string device,MonitorSession session) { try { await session.Window.PauseAsync(session.UserPaused||session.AutoPaused); if(hosts.TryGetValue(device,out var current) && ReferenceEquals(current,session)) { RefreshWorkspace(); } } catch(Exception e) { session.Error="Ошибка паузы: "+e.Message; RefreshWorkspace(); } }
    void SetPreview(string path) { if(path==currentPreview)return; currentPreview=path; var image=PreviewConverter.Load(path); ActivePreview.Source=image; ActivePlaceholder.Visibility=image==null?Visibility.Visible:Visibility.Collapsed; }
    void Remove_Click(object s,RoutedEventArgs e) { if(LibraryList.SelectedItem is not Wallpaper w)return; if(MessageBox.Show("Удалить запись из библиотеки? Исходные файлы останутся на месте; обои NOVA с этой записью будут остановлены на всех мониторах.","Удаление",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return; foreach(var p in hosts.Where(x=>x.Value.Window.Item.Id==w.Id).ToArray())CloseMonitor(p.Key,false); WorkspaceRules.RemoveWallpaper(state,w.Id); vm.Library.Remove(w); Save(); Filter_Changed(this,null!); RefreshWorkspace(); }
    async void Relink_Click(object s,RoutedEventArgs e) { if(LibraryList.SelectedItem is not Wallpaper old)return; var d=new Microsoft.Win32.OpenFileDialog { Title="Укажите файл обоев или project.json", Filter="Обои|*.mp4;*.webm;*.gif;*.html;*.htm;project.json|Все файлы|*.*" }; if(d.ShowDialog()!=true)return; try { var item=await Task.Run(()=>ProjectReader.Read(d.FileName)); if(vm.Library.Any(x=>x.Id!=old.Id && string.Equals(x.Source,item.Source,StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("Этот файл уже есть в библиотеке."); return; } item.Id=old.Id; item.Favorite=old.Favorite; int i=vm.Library.IndexOf(old); vm.Library[i]=item; LibraryList.SelectedItem=item; Save(); QueueThumbnails(new[]{item}); } catch(Exception ex) { MessageBox.Show("Не удалось указать путь: "+ex.Message); } }
    void UpdateWeather() {
        if(!weatherReady) return;
        state.DesktopSeason=(SeasonEffect)Math.Clamp(SeasonBox.SelectedIndex,0,3);
        state.DesktopIntensity=(int)SeasonIntensity.Value; state.DesktopSpeed=(int)SeasonSpeed.Value;
        desktopSnow.Configure(state.DesktopSeason,state.DesktopIntensity,state.DesktopSpeed); SeasonPreview.Configure(state.DesktopSeason,state.DesktopIntensity,state.DesktopSpeed); RefreshWorkspace(); Save();
    }
    void Season_Changed(object s,SelectionChangedEventArgs e)=>UpdateWeather();
    void WeatherSlider_Changed(object s,RoutedPropertyChangedEventArgs<double> e)=>UpdateWeather();
    void DesktopCaps_Changed(object s, RoutedEventArgs e) { if (!weatherReady) return; desktopSnow.CapsEnabled=ShowDesktopCaps.IsChecked==true; state.WindowSnowCaps=desktopSnow.CapsEnabled; Save(); }
    void ToggleDesktopSnow_Click(object s, RoutedEventArgs e) {
        if(state.AccessibleMode) { ShowNotice("Эффекты приостановлены", "Выключите режим доступности, чтобы запустить сезонный эффект."); return; }
        try { if(desktopSnow.Running) { desktopSnow.Stop(); state.DesktopSnowEnabled=false; DesktopSnowStatus.Text="Эффект выключен."; DesktopSnowButton.Content="Включить эффект"; }
        else { desktopSnow.CapsEnabled=ShowDesktopCaps.IsChecked==true; desktopSnow.Start(); state.DesktopSnowEnabled=true; state.WindowSnowCaps=desktopSnow.CapsEnabled; DesktopSnowStatus.Text="Эффект включён."; DesktopSnowButton.Content="Выключить эффект"; } Save(); RefreshWorkspace(); ShowNotice("Сезонный эффект",DesktopSnowStatus.Text); }
        catch(Exception ex) { state.DesktopSnowEnabled=false; DesktopSnowStatus.Text="Не удалось включить эффект: " + ex.Message; MessageBox.Show(DesktopSnowStatus.Text,"Эффекты",MessageBoxButton.OK,MessageBoxImage.Error); Save(); }
    }
    static readonly Dictionary<string,string> CursorRoleNames = new() { ["Arrow"]="Обычный", ["IBeam"]="Текст", ["Wait"]="Ожидание", ["AppStarting"]="Работа в фоне", ["Hand"]="Ссылка" };
    string? SelectedCursorRole => (CursorRole?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    void RefreshCursorSelection() {
        if(CursorPath==null || CursorMappings==null || CursorPreview==null || ApplyCursorButton==null) return;
        string? role=SelectedCursorRole; string? path=role!=null && chosenCursors.TryGetValue(role,out var p) ? p : null;
        CursorPath.Text=path??"Файл для роли не выбран"; ApplyCursorButton.IsEnabled=chosenCursors.Count>0;
        CursorMappings.Text=chosenCursors.Count==0?"Пока не выбрано":string.Join("\n",chosenCursors.Select(x=>$"{CursorRoleNames[x.Key]}: {x.Value}"));
        CursorPreview.Cursor=null; cursorPreview?.Dispose(); cursorPreview=null;
        if(path==null) { CursorPreviewText.Text="После выбора наведите указатель сюда для предварительного просмотра."; return; }
        try { cursorPreview=new System.Windows.Input.Cursor(path); CursorPreview.Cursor=cursorPreview; CursorPreviewText.Text="Наведите указатель на эту область для просмотра."; }
        catch { CursorPreviewText.Text="Предпросмотр недоступен; Windows проверит файл перед применением."; }
    }
    void CursorRole_Changed(object s,SelectionChangedEventArgs e)=>RefreshCursorSelection();
    void ChooseCursor_Click(object s,RoutedEventArgs e) {
        string? role=SelectedCursorRole; if(role==null)return;
        var d=new Microsoft.Win32.OpenFileDialog { Title="Выберите файл курсора", Filter="Файлы курсоров (*.cur;*.ani)|*.cur;*.ani" };
        if(d.ShowDialog()!=true)return; chosenCursors[role]=d.FileName; RefreshCursorSelection();
    }
    void RemoveCursorRole_Click(object s,RoutedEventArgs e) { if(SelectedCursorRole is string role)chosenCursors.Remove(role); RefreshCursorSelection(); }
    void ImportCursorFolder_Click(object s,RoutedEventArgs e) {
        using var dialog=new Forms.FolderBrowserDialog { Description="Папка с курсорами .cur и .ani" };
        if(dialog.ShowDialog()!=Forms.DialogResult.OK)return;
        try {
            var files=Directory.EnumerateFiles(dialog.SelectedPath).Where(p=>new[]{".cur",".ani"}.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase)).ToList();
            var found=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var role in CursorRoleNames.Keys) { var matches=files.Where(p=>string.Equals(Path.GetFileNameWithoutExtension(p),role,StringComparison.OrdinalIgnoreCase)).ToList(); if(matches.Count>1) throw new InvalidOperationException($"Для {role} найдено несколько файлов. Оставьте один или выберите вручную."); if(matches.Count==1)found[role]=matches[0]; }
            if(found.Count==0)throw new InvalidOperationException("Не найдены файлы с именами Arrow, IBeam, Wait, AppStarting или Hand. Можно выбрать каждый файл вручную.");
            foreach(var p in found)chosenCursors[p.Key]=p.Value; RefreshCursorSelection(); CursorStatus.Text=$"Из папки добавлено ролей: {found.Count}. Проверьте набор и нажмите «Применить набор».";
        } catch(Exception ex) { MessageBox.Show("Не удалось прочитать папку: "+ex.Message,"Набор курсоров"); }
    }
    void ApplyCursor_Click(object s,RoutedEventArgs e) {
        if(chosenCursors.Count==0)return;
        if(MessageBox.Show($"Изменить указатели Windows для текущего пользователя (ролей: {chosenCursors.Count})? Прежние значения будут сохранены. Не перемещайте выбранные файлы, пока они используются.","Набор курсоров",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        try { CursorStatus.Text=cursors.ApplySet(chosenCursors); ShowNotice("Курсоры применены",CursorStatus.Text); } catch(Exception ex) { CursorStatus.Text="Не удалось изменить указатели: "+ex.Message; MessageBox.Show(CursorStatus.Text,"Набор курсоров",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
    void RestoreCursor_Click(object s,RoutedEventArgs e) { if(MessageBox.Show("Восстановить все указатели, сохранённые до изменения в NOVA?", "Набор курсоров", MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return; try { CursorStatus.Text=cursors.Restore(); ShowNotice("Курсоры",CursorStatus.Text); } catch(Exception ex) { CursorStatus.Text="Не удалось восстановить указатели: "+ex.Message; MessageBox.Show(CursorStatus.Text,"Набор курсоров",MessageBoxButton.OK,MessageBoxImage.Warning); } }
    void OpenMouseSettings_Click(object s,RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("ms-settings:mousetouchpad") { UseShellExecute=true }); } catch(Exception ex) { MessageBox.Show("Не удалось открыть настройки мыши: "+ex.Message); } }
    void TaskbarColor_Click(object s, RoutedEventArgs e) {
        using var dialog = new Forms.ColorDialog { FullOpen=true };
        try { var c=System.Windows.Media.ColorConverter.ConvertFromString(TaskbarColor.Text); if (c is System.Windows.Media.Color color) dialog.Color=System.Drawing.Color.FromArgb(color.R,color.G,color.B); } catch { }
        if (dialog.ShowDialog() == Forms.DialogResult.OK) { var c=dialog.Color; TaskbarColor.Text=$"#{c.R:X2}{c.G:X2}{c.B:X2}"; TaskbarColorPreview.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R,c.G,c.B)); }
    }
    void TaskbarApply_Click(object s, RoutedEventArgs e) {
        if (MessageBox.Show("Изменить оформление основной панели задач? Функция экспериментальная: на некоторых версиях Windows 11 эффект может быть не виден. NOVA попытается восстановить исходный стиль при полном выходе; другие программы оформления могут конфликтовать.","Панель задач",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
        try {
            if (System.Windows.Media.ColorConverter.ConvertFromString(TaskbarColor.Text) is not System.Windows.Media.Color color) throw new InvalidOperationException("Неверный цвет.");
            taskbar.Apply(color.R,color.G,color.B,(byte)Math.Round(TaskbarOpacity.Value*255/100));
            TaskbarStatus.Text="Команда отправлена. Проверьте панель: Windows может скрывать или сбрасывать эффект.";
        } catch(Exception ex) { TaskbarStatus.Text="Не применено: " + ex.Message; MessageBox.Show(TaskbarStatus.Text,"Панель задач",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
    void TaskbarRestore_Click(object s, RoutedEventArgs e) { try { TaskbarStatus.Text=taskbar.Restore(); } catch(Exception ex) { TaskbarStatus.Text="Не удалось восстановить: "+ex.Message; } }
    void OpenWindowsColors_Click(object s, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("ms-settings:colors") { UseShellExecute=true }); } catch(Exception ex) { MessageBox.Show("Не удалось открыть настройки Windows: "+ex.Message); } }
    void ChooseWE_Click(object s,RoutedEventArgs e) { var d=new Microsoft.Win32.OpenFileDialog { Filter="Wallpaper Engine|wallpaper32.exe;wallpaper64.exe", Title="Укажите wallpaper32.exe или wallpaper64.exe" }; if(d.ShowDialog()==true)WEPath.Text=d.FileName; }
    void OpenData_Click(object s,RoutedEventArgs e) { try { Directory.CreateDirectory(storage.DirectoryPath); Process.Start(new ProcessStartInfo(storage.DirectoryPath){UseShellExecute=true}); } catch(Exception ex) { MessageBox.Show(ex.Message); } }
    void Snow_Changed(object s, RoutedEventArgs e) { if (SnowLayer != null && ShowSnow != null) SnowLayer.IsSnowing = !state.AccessibleMode && ShowSnow.IsChecked == true; }
    void Garland_Changed(object s, RoutedEventArgs e) { if (GarlandLayer != null && ShowGarland != null) GarlandLayer.IsLit = !state.AccessibleMode && ShowGarland.IsChecked == true; }
    void SaveSettings_Click(object s,RoutedEventArgs e) { try { if(!int.TryParse(WEIndex.Text,out var idx) || idx<0) throw new InvalidOperationException("Индекс монитора должен быть неотрицательным целым числом."); AutostartService.Set(AutoStart.IsChecked==true); state.AutoStart=AutoStart.IsChecked==true; state.SnowEnabled=ShowSnow.IsChecked==true; state.GarlandEnabled=ShowGarland.IsChecked==true; state.PauseFullscreen=PauseFullscreen.IsChecked==true; state.PauseLocked=PauseLocked.IsChecked==true; state.AllowNetwork=AllowNetwork.IsChecked==true; state.Volume=(int)Volume.Value; state.WallpaperEnginePath=WEPath.Text; state.WallpaperEngineMonitor=idx; foreach(var session in hosts.Values)session.Window.SetVolume(state.Volume); if(Save())ShowNotice("Настройки сохранены", "Сетевые разрешения вступят в силу после повторного применения веб-обоев."); else if(!stateSafe)ShowNotice("Сохранение отключено", "Исходный файл настроек не перезаписан из-за ошибки чтения.",true); } catch(Exception ex) { MessageBox.Show("Не удалось сохранить настройки: "+ex.Message); } }
}

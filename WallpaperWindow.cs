using System.IO;
using System.Windows;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Nova.Core;
using Forms = System.Windows.Forms;

namespace Nova.App.Services;
public sealed class WallpaperWindow : Window {
    LibVLC? vlc; MediaPlayer? player; Media? media; WebView2? web;
    public IntPtr Worker { get; private set; }
    public Wallpaper Item { get; }
    public string Monitor { get; }
    public event Action<string>? PlaybackError;
    public WallpaperWindow(Wallpaper item, Forms.Screen screen) {
        Item=item; Monitor=screen.DeviceName;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; ShowActivated=false;
        Width=screen.Bounds.Width; Height=screen.Bounds.Height;
        Background=System.Windows.Media.Brushes.Black;
    }
    public async Task StartAsync(Forms.Screen screen, bool allowNetwork, int volume) {
        if (!File.Exists(Item.Entry)) throw new FileNotFoundException("Основной файл обоев не найден.", Item.Entry);
        if (Item.Kind == WallpaperKind.Video) {
            LibVLCSharp.Shared.Core.Initialize(); vlc = new LibVLC("--no-video-title-show"); player = new MediaPlayer(vlc) { Volume = Math.Clamp(volume,0,100), EnableHardwareDecoding = true };
            player.EncounteredError += (_,_) => Dispatcher.BeginInvoke(() => PlaybackError?.Invoke("VLC не смог воспроизвести файл. Проверьте формат и кодек."));
            player.EndReached += (_,_) => Dispatcher.BeginInvoke(() => { try { player?.Stop(); if (media != null) player?.Play(media); } catch { } });
            media = new Media(vlc, new Uri(Item.Entry)); Content = new VideoView { MediaPlayer=player };
            Show(); Worker=await DesktopHost.AttachAsync(this, screen); if (!player.Play(media)) throw new InvalidOperationException("VLC не смог начать воспроизведение.");
        } else if (Item.Kind == WallpaperKind.Web) {
            web = new WebView2(); Content=web; Show(); Worker=await DesktopHost.AttachAsync(this, screen);
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVA", "WebView2"));
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled=false; core.Settings.AreDevToolsEnabled=false; core.Settings.AreDefaultScriptDialogsEnabled=false;
            core.IsMuted = volume == 0; // WebView2 exposes mute, not per-view volume.
            core.PermissionRequested += (_, e) => { e.State=CoreWebView2PermissionState.Deny; };
            core.NewWindowRequested += (_, e) => { e.Handled=true; };
            core.DownloadStarting += (_, e) => { e.Cancel=true; };
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => {
                if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var u)) { e.Response=env.CreateWebResourceResponse(null,403,"Forbidden", ""); return; }
                bool local = u.Scheme == "https" && u.Host.Equals("nova-wallpaper.local", StringComparison.OrdinalIgnoreCase);
                bool safeScheme = u.Scheme is "data" or "blob" or "about";
                if (!local && !safeScheme && (!allowNetwork || (u.Scheme != "http" && u.Scheme != "https"))) e.Response=env.CreateWebResourceResponse(null,403,"Forbidden", "");
            };
            core.NavigationStarting += (_, e) => {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) || !(u.Scheme == "https" && u.Host.Equals("nova-wallpaper.local",StringComparison.OrdinalIgnoreCase))) e.Cancel=true;
            };
            core.NavigationCompleted += async (_, e) => {
                if (!e.IsSuccess) { PlaybackError?.Invoke("Ошибка загрузки веб-обоев: " + e.WebErrorStatus); return; }
                try {
                    if (Path.GetFileName(Item.Source).Equals("project.json", StringComparison.OrdinalIgnoreCase)) {
                        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Item.Source));
                        if (doc.RootElement.TryGetProperty("general", out var general) && general.ValueKind == System.Text.Json.JsonValueKind.Object && general.TryGetProperty("properties", out var properties) && properties.ValueKind == System.Text.Json.JsonValueKind.Object) {
                            string data = properties.GetRawText();
                            await core.ExecuteScriptAsync("window.wallpaperPropertyListener?.applyUserProperties?.(" + data + ")");
                        }
                    }
                } catch (Exception ex) { PlaybackError?.Invoke("Не удалось передать свойства веб-проекта: " + ex.Message); }
            };
            string root = Path.GetFileName(Item.Source).Equals("project.json", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(Item.Source)! : Path.GetDirectoryName(Item.Entry)!;
            string rel = Path.GetRelativePath(root, Item.Entry);
            if (rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Главный файл веб-проекта находится вне папки проекта.");
            core.SetVirtualHostNameToFolderMapping("nova-wallpaper.local", root, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Navigate("https://nova-wallpaper.local/" + string.Join("/", rel.Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString)));
        } else throw new InvalidOperationException("Этот тип не поддерживается встроенным движком.");
    }
    public async Task PauseAsync(bool pause) {
        if (player != null) { player.SetPause(pause); return; }
        if (web?.CoreWebView2 != null) {
            // Cooperative event plus CSS animation pause. Arbitrary JS / audio may continue.
            string flag = pause ? "true" : "false";
            await web.CoreWebView2.ExecuteScriptAsync($"(()=>{{let p={flag};window.wallpaperPropertyListener?.setPaused?.(p);document.getAnimations().forEach(a=>p?a.pause():a.play());document.querySelectorAll('video,audio').forEach(e=>{{if(p){{e.dataset.novaWasPlaying=(!e.paused).toString();e.pause()}}else if(e.dataset.novaWasPlaying==='true')e.play().catch(()=>{{}})}})}})()");
        }
    }
    public void SetVolume(int volume) { if (player != null) player.Volume=Math.Clamp(volume,0,100); if (web?.CoreWebView2 != null) web.CoreWebView2.IsMuted=volume==0; }
    protected override void OnClosed(EventArgs e) { try { player?.Stop(); } catch {} web?.Dispose(); media?.Dispose(); player?.Dispose(); vlc?.Dispose(); base.OnClosed(e); }
}

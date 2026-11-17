using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Nova.App.Views;
using Nova.Core;
using Forms = System.Windows.Forms;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;

namespace Nova.App.Services;
/// <summary>Opt-in desktop overlays; draggable caps follow visible normal windows.</summary>
public sealed class DesktopSnowService : IDisposable {
    readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(700) };
    readonly Dictionary<string,SnowWindow> snow=new();
    readonly Dictionary<IntPtr,(int X,int Y)> offsets=new();
    readonly Dictionary<IntPtr,SnowCapWindow> caps=new();
    readonly Dictionary<IntPtr,RECT> targets=new();
    bool running, locked, capsEnabled=true; SeasonEffect effect=SeasonEffect.Snow; int intensity=100, speed=100;
    string screensKey="";
    public bool Running => running;
    public bool CapsEnabled { get=>capsEnabled; set { capsEnabled=value; if(!value) foreach(var c in caps.Values)c.Hide(); } }
    public DesktopSnowService() { timer.Tick+=(_,_)=>Refresh(); }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
    delegate bool EnumProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder name,int count);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
    public void Configure(SeasonEffect kind,int density,int velocity) {
        effect=Enum.IsDefined(kind)?kind:SeasonEffect.Snow; intensity=Math.Clamp(density,25,200); speed=Math.Clamp(velocity,25,200);
        foreach(var w in snow.Values)w.Configure(effect,intensity,speed);
        if(effect!=SeasonEffect.Snow)foreach(var c in caps.Values)c.Hide();
    }
    public void Start() {
        if(running)return;
        running=true;
        try { Rebuild(); timer.Start(); Refresh(); if(!running)throw new InvalidOperationException("Не удалось запустить слой снега."); }
        catch { Stop(); throw; }
    }
    public void Stop() { running=false; timer.Stop(); foreach(var w in snow.Values)w.Close(); snow.Clear(); foreach(var c in caps.Values)c.Close(); caps.Clear(); targets.Clear(); offsets.Clear(); screensKey=""; }
    public void SetLocked(bool value) { locked=value; if(running)Refresh(); }
    public void Dispose()=>Stop();
    static string Key() => string.Join(";",Forms.Screen.AllScreens.Select(s=>$"{s.DeviceName}:{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}:{s.WorkingArea.X},{s.WorkingArea.Y},{s.WorkingArea.Width},{s.WorkingArea.Height}"));
    void Rebuild() {
        foreach(var w in snow.Values)w.Close(); snow.Clear();
        foreach(var screen in Forms.Screen.AllScreens) { var w=new SnowWindow(); w.Configure(effect,intensity,speed); w.Show(); w.Place(screen); snow.Add(screen.DeviceName,w); }
        screensKey=Key();
    }
    void Refresh() {
        if(!running)return;
        try {
            if(screensKey!=Key())Rebuild();
            var fg=GetForegroundWindow();
            foreach(var screen in Forms.Screen.AllScreens) {
                if(!snow.TryGetValue(screen.DeviceName,out var w))continue;
                bool full=fg!=IntPtr.Zero && DesktopHost.IsFullscreen(screen) && (GetWindowLongPtr(fg,-16).ToInt64() & 0x00C00000)==0;
                bool show=!locked && !full;
                if(show && !w.IsVisible) { w.Show(); w.Place(screen); }
                else if(!show && w.IsVisible)w.Hide();
            }
            RefreshCaps();
        } catch(Exception ex) { Stop(); Error?.Invoke("Снег остановлен: " + ex.Message); }
    }
    public event Action<string>? Error;
    readonly record struct WindowInfo(IntPtr Handle, RECT Bounds, long Style);
    static bool Overlaps(RECT a, RECT b) => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
    void RefreshCaps() {
        if(!capsEnabled||locked||effect!=SeasonEffect.Snow) { foreach(var c in caps.Values)c.Hide(); return; }
        // EnumWindows is ordered top to bottom. Ignore our overlays and skip obscured caps.
        var windows=new List<WindowInfo>();
        EnumWindows((h,_)=> {
            if(!IsWindowVisible(h)||IsIconic(h)||!GetWindowRect(h,out var r)||r.Right<=r.Left||r.Bottom<=r.Top)return true;
            GetWindowThreadProcessId(h,out uint pid);
            if(pid==(uint)Environment.ProcessId) {
                var main=System.Windows.Application.Current?.MainWindow;
                if(main!=null && new WindowInteropHelper(main).Handle==h) windows.Add(new WindowInfo(h,r,0));
                return true;
            }
            if(DwmGetWindowAttribute(h,14,out int cloaked,sizeof(int))==0 && cloaked!=0)return true;
            var name=new System.Text.StringBuilder(128);GetClassName(h,name,name.Capacity);
            if(name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return true;
            windows.Add(new WindowInfo(h,r,GetWindowLongPtr(h,-16).ToInt64()));return true;
        },IntPtr.Zero);
        var keep=new HashSet<IntPtr>(); int placed=0;
        for(int i=0;i<windows.Count && placed<10;i++) {
            var item=windows[i]; var rect=item.Bounds; int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
            if((item.Style & 0x00C00000)==0 || width<240 || height<120)continue;
            var screen=Forms.Screen.FromHandle(item.Handle); var monitor=screen.WorkingArea;
            var front=GetForegroundWindow(); bool full=front!=IntPtr.Zero && DesktopHost.IsFullscreen(screen) && (GetWindowLongPtr(front,-16).ToInt64() & 0x00C00000)==0;
            if(rect.Top<monitor.Top-4 || full)continue;
            uint dpi=GetDpiForWindow(item.Handle);if(dpi==0)dpi=96;
            int cw=(int)Math.Round(160*dpi/96.0), ch=(int)Math.Round(30*dpi/96.0);
            if(!offsets.TryGetValue(item.Handle,out var off))off=(Math.Min(45,Math.Max(0,width-cw)),-ch/2);
            int x=rect.Left+Math.Clamp(off.X,0,Math.Max(0,width-cw));
            int y=rect.Top+Math.Clamp(off.Y,-ch,Math.Min(70,height-ch));
            var region=new RECT { Left=x,Top=y,Right=x+cw,Bottom=y+ch };
            bool covered=false; for(int j=0;j<i;j++) if(Overlaps(region,windows[j].Bounds)) { covered=true; break; }
            keep.Add(item.Handle); targets[item.Handle]=rect;
            if(covered && (!caps.TryGetValue(item.Handle,out var existing) || !existing.IsDragging)) { if(existing!=null)existing.Hide(); continue; }
            if(!caps.TryGetValue(item.Handle,out var c)) { var handle=item.Handle; c=new SnowCapWindow((px,py)=>OnCapDragged(handle,px,py)); caps.Add(handle,c); }
            if(!c.IsVisible)c.Show(); if(!c.IsDragging)c.Place(x,y,cw,ch); placed++;
        }
        foreach(var h in caps.Keys.Where(h=>!keep.Contains(h)).ToArray()) { caps[h].Close();caps.Remove(h);targets.Remove(h);offsets.Remove(h); }
    }
    void OnCapDragged(IntPtr target,int x,int y) {
        if(targets.TryGetValue(target,out var rect)) offsets[target]=(x-rect.Left,y-rect.Top);
    }
    sealed class SnowWindow : Window {
        readonly SnowOverlay overlay = new();
        public void Configure(SeasonEffect effect,int intensity,int speed) => overlay.Configure(effect,intensity,speed);
        public SnowWindow() {
            WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true; Background=System.Windows.Media.Brushes.Transparent;
            Topmost=true; ShowInTaskbar=false; ShowActivated=false; Content=overlay;
            SourceInitialized+=(_,_)=>NativeStyles.Configure(new WindowInteropHelper(this).Handle,true);
        }
        public void Place(Forms.Screen screen) { var r=screen.WorkingArea; if(!SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),r.X,r.Y,r.Width,r.Height,0x0010|0x0040))throw new InvalidOperationException("Не удалось разместить снег на экране."); }
    }
    sealed class SnowCapWindow : Window {
        readonly Action<int,int> moved; readonly DispatcherTimer dragTimer = new() { Interval=TimeSpan.FromMilliseconds(20) }; bool dragging; public bool IsDragging => dragging; System.Drawing.Point startMouse; int startX,startY; HwndSourceHook? hook;
        public SnowCapWindow(Action<int,int> moved) {
            this.moved=moved; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true; Background=System.Windows.Media.Brushes.Transparent;
            Topmost=true; ShowInTaskbar=false; ShowActivated=false; Width=160; Height=30; Cursor=System.Windows.Input.Cursors.SizeAll;
            Content=new CapVisual();
            SourceInitialized+=(_,_)=> { var hwnd=new WindowInteropHelper(this).Handle; NativeStyles.Configure(hwnd,false); var src=HwndSource.FromHwnd(hwnd); if(src!=null) { hook=(IntPtr h,int m,IntPtr wp,IntPtr lp,ref bool handled)=> { if(m==0x0021) { handled=true; return new IntPtr(3); } return IntPtr.Zero; }; src.AddHook(hook); } };
            MouseLeftButtonDown+=(_,e)=> { startMouse=Forms.Cursor.Position; var h=new WindowInteropHelper(this).Handle; if(GetWindowRect(h,out var r)) { startX=r.Left;startY=r.Top;dragging=true;CaptureMouse();dragTimer.Start();e.Handled=true; } };
            MouseMove+=(_,e)=> { if(!dragging)return; MoveDrag();e.Handled=true; };
            MouseLeftButtonUp+=(_,e)=> { if(!dragging)return; EndDrag();e.Handled=true; };
            // NOACTIVATE keeps the user's window active; polling also works if mouse capture
            // is limited to the cap's bounds because our window is not foreground.
            dragTimer.Tick+=(_,_)=> { if(!dragging)return; if((GetAsyncKeyState(1)&0x8000)==0)EndDrag(); else MoveDrag(); };
            Closed+=(_,_)=>dragTimer.Stop();
        }
        void MoveDrag() { var p=Forms.Cursor.Position; int x=startX+p.X-startMouse.X,y=startY+p.Y-startMouse.Y; if(GetWindowRect(new WindowInteropHelper(this).Handle,out var r))Place(x,y,r.Right-r.Left,r.Bottom-r.Top); moved(x,y); }
        void EndDrag() { dragging=false;dragTimer.Stop();if(IsMouseCaptured)ReleaseMouseCapture(); }
        public void Place(int x,int y,int w,int h) { SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),x,y,w,h,0x0010|0x0040); }
    }
    sealed class CapVisual : FrameworkElement {
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc); var g=new StreamGeometry();
            using(var c=g.Open()) { c.BeginFigure(new Point(2,23),true,true); c.BezierTo(new Point(12,11),new Point(23,17),new Point(31,12),true,false); c.BezierTo(new Point(45,0),new Point(54,13),new Point(70,8),true,false); c.BezierTo(new Point(87,0),new Point(99,14),new Point(116,10),true,false); c.BezierTo(new Point(135,3),new Point(142,16),new Point(157,21),true,false); c.LineTo(new Point(157,26),true,false); c.BezierTo(new Point(100,29),new Point(50,24),new Point(2,27),true,false); }
            g.Freeze(); var snow=new SolidColorBrush(Color.FromArgb(240,232,242,250)); snow.Freeze(); dc.DrawGeometry(snow,null,g);
            var shade=new Pen(new SolidColorBrush(Color.FromArgb(150,162,184,202)),1); shade.Freeze(); dc.DrawLine(shade,new Point(12,25),new Point(150,25));
        }
    }
    static class NativeStyles {
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetLong(IntPtr h,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] static extern IntPtr SetLong(IntPtr h,int index,IntPtr value);
        public static void Configure(IntPtr h,bool clickThrough) { long ex=GetLong(h,-20).ToInt64(); ex|=0x00000080|0x08000000; if(clickThrough)ex|=0x20; SetLong(h,-20,new IntPtr(ex)); if(clickThrough && (GetLong(h,-20).ToInt64() & 0x20)==0) throw new InvalidOperationException("Не удалось включить пропуск кликов через слой снега."); }
    }
}

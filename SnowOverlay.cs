using Pen = System.Windows.Media.Pen;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Nova.Core;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Nova.App.Views;

/// <summary>A decorative, non-interactive overlay for the NOVA window only.</summary>
public sealed class SnowOverlay : FrameworkElement {
    public static readonly DependencyProperty IsSnowingProperty = DependencyProperty.Register(
        nameof(IsSnowing), typeof(bool), typeof(SnowOverlay),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((SnowOverlay)d).UpdateTimer()));
    public bool IsSnowing { get => (bool)GetValue(IsSnowingProperty); set => SetValue(IsSnowingProperty, value); }

    public SeasonEffect Season { get; private set; } = SeasonEffect.Snow;
    public int Intensity { get; private set; } = 100;
    public int SpeedPercent { get; private set; } = 100;
    public void Configure(SeasonEffect effect, int intensity, int speed) {
        Season = Enum.IsDefined(effect) ? effect : SeasonEffect.Snow;
        Intensity = Math.Clamp(intensity, 25, 200); SpeedPercent = Math.Clamp(speed, 25, 200);
        EnsureFlakes(); InvalidateVisual();
    }
    sealed class Flake { public double X, Y, Speed, Radius, Phase, Drift; public int Shade; }
    readonly List<Flake> flakes = new();
    readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    static readonly Brush[] FlakeBrushes = {
        Frozen(Color.FromArgb(125, 233, 242, 250)),
        Frozen(Color.FromArgb(165, 245, 248, 252)),
        Frozen(Color.FromArgb(210, 250, 251, 253))
    };
    static readonly Brush[] LeafBrushes = { Frozen(Color.FromArgb(200,213,115,45)), Frozen(Color.FromArgb(200,172,78,43)), Frozen(Color.FromArgb(200,224,169,73)) };
    static readonly Brush[] PetalBrushes = { Frozen(Color.FromArgb(190,247,190,210)), Frozen(Color.FromArgb(195,255,218,228)), Frozen(Color.FromArgb(190,233,164,189)) };
    static readonly Pen RainPen = FrozenPen(Frozen(Color.FromArgb(145,168,198,222)),1.1);
    static readonly Geometry LeafShape = MakeLeaf();
    static Pen FrozenPen(Brush brush,double width) { var p=new Pen(brush,width);p.Freeze();return p; }
    static Geometry MakeLeaf() { var g=new StreamGeometry(); using(var c=g.Open()) { c.BeginFigure(new Point(0,0),true,true);c.QuadraticBezierTo(new Point(5,-5),new Point(10,0),true,false);c.QuadraticBezierTo(new Point(5,5),new Point(0,0),true,false); } g.Freeze();return g; }
    static readonly Brush DistantSnow = Frozen(Color.FromArgb(120, 169, 185, 199));
    static readonly Brush FrontSnow = Frozen(Color.FromArgb(210, 224, 234, 241));
    static readonly Brush SnowHighlight = Frozen(Color.FromArgb(105, 255, 255, 255));
    Window? owner;
    long last;
    double elapsed;

    static SolidColorBrush Frozen(Color color) { var b = new SolidColorBrush(color); b.Freeze(); return b; }
    public SnowOverlay() {
        IsHitTestVisible = false;
        ClipToBounds = true;
        timer.Tick += Tick;
        Loaded += (_, _) => {
            owner = Window.GetWindow(this);
            if (owner != null) owner.StateChanged += OwnerStateChanged;
            UpdateTimer();
        };
        Unloaded += (_, _) => {
            timer.Stop();
            if (owner != null) owner.StateChanged -= OwnerStateChanged;
            owner = null;
        };
        IsVisibleChanged += (_, _) => UpdateTimer();
        SizeChanged += (_, _) => { EnsureFlakes(); InvalidateVisual(); };
    }
    void OwnerStateChanged(object? sender, EventArgs e) => UpdateTimer();
    void UpdateTimer() {
        bool active = IsSnowing && IsLoaded && IsVisible && owner?.WindowState != WindowState.Minimized;
        if (active) { EnsureFlakes(); last = Stopwatch.GetTimestamp(); if (!timer.IsEnabled) timer.Start(); }
        else timer.Stop();
        InvalidateVisual();
    }
    void EnsureFlakes() {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        // A bounded number of particles keeps the overlay lightweight at high DPI.
        int baseCount = Math.Clamp((int)(ActualWidth * ActualHeight / 13000), 32, 90);
        int target = Math.Clamp((int)Math.Round(baseCount * Intensity / 100.0), 8, 180);
        while (flakes.Count < target) flakes.Add(NewFlake(false));
        if (flakes.Count > target) flakes.RemoveRange(target, flakes.Count - target);
    }
    Flake NewFlake(bool atTop) {
        var r = Random.Shared;
        return new Flake {
            X = r.NextDouble() * Math.Max(1, ActualWidth),
            Y = atTop ? -5 - r.NextDouble() * 40 : r.NextDouble() * Math.Max(1, ActualHeight),
            Speed = 16 + r.NextDouble() * 27,
            Radius = 0.8 + r.NextDouble() * 1.6,
            Phase = r.NextDouble() * Math.PI * 2,
            Drift = 3 + r.NextDouble() * 9,
            Shade = r.Next(FlakeBrushes.Length)
        };
    }
    void Tick(object? sender, EventArgs e) {
        long now = Stopwatch.GetTimestamp();
        double delta = Math.Clamp((now - last) / (double)Stopwatch.Frequency, 0, 0.08);
        last = now; elapsed += delta;
        for (int i = 0; i < flakes.Count; i++) {
            var f = flakes[i];
            double speed=SpeedPercent/100.0;
            f.Y += f.Speed * delta * speed * (Season == SeasonEffect.Rain ? 4.0 : 1.0);
            f.X += (Season == SeasonEffect.Rain ? -18 : Math.Sin(elapsed * 0.7 + f.Phase) * f.Drift) * delta * speed;
            if (f.Y > ActualHeight + 5 || f.X < -12 || f.X > ActualWidth + 12) flakes[i] = NewFlake(true);
        }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);
        if (!IsSnowing || ActualWidth <= 0 || ActualHeight <= 0) return;
        foreach (var f in flakes) {
            if (Season == SeasonEffect.Snow) dc.DrawEllipse(FlakeBrushes[f.Shade], null, new Point(f.X, f.Y), f.Radius, f.Radius);
            else if (Season == SeasonEffect.Rain) dc.DrawLine(RainPen,new Point(f.X,f.Y),new Point(f.X-3,f.Y+10+f.Radius*3));
            else {
                dc.PushTransform(new TranslateTransform(f.X,f.Y));
                dc.PushTransform(new RotateTransform((elapsed*30+f.Phase*100)%360));
                if (Season == SeasonEffect.Leaves) dc.DrawGeometry(LeafBrushes[f.Shade],null,LeafShape);
                else dc.DrawEllipse(PetalBrushes[f.Shade],null,new Point(0,0),f.Radius*2.1,f.Radius*1.2);
                dc.Pop();dc.Pop();
            }
        }
        if (Season != SeasonEffect.Snow) return;
        DrawDrift(dc, DistantSnow, ActualWidth, ActualHeight, 30, 0);
        DrawDrift(dc, FrontSnow, ActualWidth, ActualHeight, 15, 1);
        // A slim highlight gives the banks a soft edge without obscuring controls.
        dc.DrawRectangle(SnowHighlight, null, new Rect(0, Math.Max(0, ActualHeight - 2), ActualWidth, Math.Min(2, ActualHeight)));
    }
    static void DrawDrift(DrawingContext dc, Brush brush, double width, double height, double depth, int layer) {
        var g = new StreamGeometry();
        using (var c = g.Open()) {
            c.BeginFigure(new Point(0, height), true, true);
            if (layer == 0) {
                c.LineTo(new Point(0, height - depth * .75), true, false);
                c.BezierTo(new Point(width*.15, height-depth*1.6), new Point(width*.21, height-depth*.05), new Point(width*.37, height-depth*.75), true, false);
                c.BezierTo(new Point(width*.52, height-depth*1.5), new Point(width*.64, height-depth*.1), new Point(width*.77, height-depth*.8), true, false);
                c.BezierTo(new Point(width*.89, height-depth*1.35), new Point(width*.97, height-depth*.5), new Point(width, height-depth*.7), true, false);
            } else {
                c.LineTo(new Point(0, height - depth), true, false);
                c.BezierTo(new Point(width*.12, height-depth*2.0), new Point(width*.26, height-depth*.45), new Point(width*.42, height-depth), true, false);
                c.BezierTo(new Point(width*.6, height-depth*1.6), new Point(width*.73, height-depth*.2), new Point(width*.88, height-depth*1.15), true, false);
                c.BezierTo(new Point(width*.94, height-depth*1.5), new Point(width*.98, height-depth*1.25), new Point(width, height-depth), true, false);
            }
            c.LineTo(new Point(width, height), true, false);
        }
        g.Freeze(); dc.DrawGeometry(brush, null, g);
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;

namespace Nova.App.Views;

/// <summary>Non-interactive festive lights, drawn only in the application window.</summary>
public sealed class GarlandOverlay : FrameworkElement {
    public static readonly DependencyProperty IsLitProperty = DependencyProperty.Register(
        nameof(IsLit), typeof(bool), typeof(GarlandOverlay),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((GarlandOverlay)d).UpdateTimer()));
    public bool IsLit { get => (bool)GetValue(IsLitProperty); set => SetValue(IsLitProperty, value); }

    const double Spacing = 68;
    readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
    static readonly Brush Cable = Frozen(Color.FromArgb(165, 140, 121, 100));
    static readonly Pen CablePen = FrozenPen(Cable, 1);
    static readonly Brush[] Amber = MakeBrushes(255, 157, 77);
    static readonly Brush[] WarmWhite = MakeBrushes(255, 226, 179);
    static readonly Brush Glow = Frozen(Color.FromArgb(24, 255, 173, 91));
    Window? owner;
    long last;
    double elapsed;

    static SolidColorBrush Frozen(Color color) { var b = new SolidColorBrush(color); b.Freeze(); return b; }
    static Pen FrozenPen(Brush brush, double thickness) { var p = new Pen(brush, thickness); p.Freeze(); return p; }
    static Brush[] MakeBrushes(byte red, byte green, byte blue) => Enumerable.Range(0, 16)
        .Select(i => (Brush)Frozen(Color.FromArgb((byte)(85 + i * 10), red, green, blue))).ToArray();

    public GarlandOverlay() {
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
    }
    void OwnerStateChanged(object? sender, EventArgs e) => UpdateTimer();
    void UpdateTimer() {
        bool active = IsLit && IsLoaded && IsVisible && owner?.WindowState != WindowState.Minimized;
        if (active) { last = Stopwatch.GetTimestamp(); if (!timer.IsEnabled) timer.Start(); }
        else timer.Stop();
        InvalidateVisual();
    }
    void Tick(object? sender, EventArgs e) {
        var now = Stopwatch.GetTimestamp();
        elapsed += Math.Clamp((now - last) / (double)Stopwatch.Frequency, 0, .1);
        last = now;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);
        if (!IsLit || ActualWidth <= 0) return;
        var wire = new StreamGeometry();
        using (var c = wire.Open()) {
            c.BeginFigure(new Point(0, 5), false, false);
            for (double x = 0; x < ActualWidth; x += Spacing)
                c.QuadraticBezierTo(new Point(x + Spacing/2, 15), new Point(x + Spacing, 5), true, false);
        }
        wire.Freeze();
        dc.DrawGeometry(null, CablePen, wire);
        int index = 0;
        for (double x = Spacing / 2; x < ActualWidth; x += Spacing, index++) {
            // Quadratic midpoint at y=10; short stem is drawn to the lamp.
            dc.DrawLine(CablePen, new Point(x, 10), new Point(x, 13));
            double light = .5 + .5 * Math.Sin(elapsed * .8 + index * 1.17);
            int level = Math.Clamp((int)Math.Round(light * 15), 0, 15);
            var color = index % 2 == 0 ? Amber[level] : WarmWhite[level];
            dc.DrawEllipse(Glow, null, new Point(x, 16), 6, 6);
            dc.DrawEllipse(color, null, new Point(x, 16), 2.5, 3.2);
        }
    }
}

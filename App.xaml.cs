using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
namespace Nova.App;
public partial class App : System.Windows.Application {
    public static bool AnimationsEnabled { get; set; } = true;
    void HoverEnter(object sender, System.Windows.Input.MouseEventArgs e) { if(AnimationsEnabled && sender is UIElement element && element.IsEnabled) element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.88,TimeSpan.FromMilliseconds(130))); }
    void HoverLeave(object sender, System.Windows.Input.MouseEventArgs e) { if(sender is UIElement element) { if(AnimationsEnabled) element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(170))); else element.BeginAnimation(UIElement.OpacityProperty,null); } }
}

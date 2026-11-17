using System.Windows.Media;
using Nova.Core;
namespace Nova.App.Services;
public static class ThemeService {
    public static void Apply(AccentTheme theme,int strength) {
        var colors=theme switch {
            AccentTheme.Blue => new[]{"#67BDF7","#A6DCFF","#203344","#497A9B"},
            AccentTheme.Purple => new[]{"#BA96FF","#D6C3FF","#302740","#7C659C"},
            AccentTheme.Mint => new[]{"#65D9B1","#A3F1D6","#1F3B33","#497F6D"},
            _ => new[]{"#FF7311","#FFAD76","#3A2A20","#8B5A39"}
        };
        string[] keys={"NovaAccent","NovaAccentLight","NovaAccentSurface","NovaAccentBorder"};
        double saturation=ColorThemeRules.ClampStrength(strength)/100.0;
        for(int i=0;i<keys.Length;i++) { var c=(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i])!;
            double gray=0.2126*c.R+0.7152*c.G+0.0722*c.B;
            byte Mix(byte x)=>(byte)Math.Clamp((int)Math.Round(gray+(x-gray)*saturation),0,255);
            var b=new SolidColorBrush(System.Windows.Media.Color.FromRgb(Mix(c.R),Mix(c.G),Mix(c.B)));b.Freeze();System.Windows.Application.Current.Resources[keys[i]]=b;
        }
    }
}

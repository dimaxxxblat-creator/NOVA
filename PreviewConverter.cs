using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Nova.Core;
namespace Nova.App.Views;
/// <summary>Loads only local still images on demand; invalid/missing previews leave the card placeholder.</summary>
public sealed class PreviewConverter : IValueConverter {
    public static BitmapSource? Load(string? path) {
        if(string.IsNullOrWhiteSpace(path)) return null;
        try {
            if(!Path.IsPathFullyQualified(path) || !File.Exists(path)) return null;
            var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.DecodePixelWidth=800; image.UriSource=new Uri(path,UriKind.Absolute); image.EndInit(); image.Freeze(); return image;
        } catch { return null; }
    }
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture) => value is string path ? Load(path) ?? (object)DependencyProperty.UnsetValue : DependencyProperty.UnsetValue;
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

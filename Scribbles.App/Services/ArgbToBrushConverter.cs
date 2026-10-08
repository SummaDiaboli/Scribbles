using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Scribbles.App.Services;

/// <summary>0xAARRGGBB → SolidColorBrush.</summary>
public sealed class ArgbToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        uint argb = value is uint u ? u : 0xFF000000;
        return new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

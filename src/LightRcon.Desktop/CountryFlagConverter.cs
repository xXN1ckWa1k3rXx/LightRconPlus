using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightRcon.Desktop;

public sealed class CountryFlagConverter : IValueConverter
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string code || code.Length != 2 || !code.All(char.IsAsciiLetter)) return null;
        if (Cache.TryGetValue(code, out var cached)) return cached;
        ImageSource? flag = null;
        try
        {
            var bitmap = new BitmapImage(new Uri($"pack://application:,,,/LightRcon.Desktop;component/Assets/Flags/{code.ToLowerInvariant()}.png"));
            bitmap.Freeze(); flag = bitmap;
        }
        catch (IOException) { /* Unknown or retired country code: keep the text label. */ }
        Cache[code] = flag;
        return flag;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

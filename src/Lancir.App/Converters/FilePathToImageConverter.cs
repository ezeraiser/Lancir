using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Lancir.App.Converters;

public sealed class FilePathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            int.TryParse(parameter as string, out var webpWidth);
            return Lancir.App.Services.ImageLoader.DecodeFileNow(path, webpWidth);
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // a file replaced under the same path must not return the old picture
        image.UriSource = new Uri(path, UriKind.Absolute);

        // ConverterParameter: the width the picture is shown at; decoding at that size keeps big art from filling memory.
        if (int.TryParse(parameter as string, out var decodeWidth) && decodeWidth > 0)
        {
            image.DecodePixelWidth = decodeWidth;
        }

        image.EndInit();
        image.Freeze();
        return image;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

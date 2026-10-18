using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Lancir.App.Converters;

/// <summary>Loads a remote image URL for preview purposes (e.g. the match picker's cover thumbnails).</summary>
public sealed class UrlToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            var isFile = uri.IsFile;

            // Local files are read at once (no file handle kept); web pictures load in the background.
            image.CacheOption = isFile ? BitmapCacheOption.OnLoad : BitmapCacheOption.OnDemand;
            image.UriSource = uri;
            if (int.TryParse(parameter as string, out var decodeWidth) && decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }

            image.EndInit();
            if (isFile)
            {
                image.Freeze();
            }

            return image;
        }
        catch (Exception)
        {
            // Broken/unreachable image URL - fall back to no image rather than crashing the picker.
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

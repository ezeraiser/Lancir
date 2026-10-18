using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lancir.App.Services;

/// <summary>
/// Extracts an executable's associated shell icon (the same one Explorer shows) as a WPF
/// ImageSource, so scan review and library views can show real per-game icons.
/// </summary>
public static class ExecutableIconExtractor
{
    public static ImageSource? TryExtractIcon(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            var imageSource = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            imageSource.Freeze();
            return imageSource;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

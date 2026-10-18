using SkiaSharp;

namespace Lancir.Core.Services;

/// <summary>
/// Turns JPEG/PNG pictures into WebP: much smaller at the same look (measured on real Steam screenshots: about 40% of the
/// JPEG at quality 80, SSIM 0.97), with transparency. A picture is never enlarged, and wider ones are scaled down to the
/// widest size the app can show.
/// </summary>
public static class ImageCompressor
{
    public const string Extension = ".webp";

    public const int ScreenshotMaxWidth = 1920;
    public const int BackgroundMaxWidth = 1920;
    public const int CoverMaxWidth = 900;

    /// <summary>True for the picture types that are converted (WebP files and clips are left alone).</summary>
    public static bool IsCompressible(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The widest size a cached picture is useful at, from what it is (backgrounds are wide, covers are narrow).</summary>
    public static int MaxWidthFor(string path) =>
        Path.GetFileNameWithoutExtension(path).Contains("background", StringComparison.OrdinalIgnoreCase)
            ? BackgroundMaxWidth
            : Path.GetFullPath(path).StartsWith(AppPaths.SteamMediaFolder, StringComparison.OrdinalIgnoreCase)
                ? ScreenshotMaxWidth
                : CoverMaxWidth;

    /// <summary>
    /// WebP bytes for a picture, or null when it cannot be decoded, the result cannot be read back, or it is not smaller than
    /// the original (so the original should be kept as it is).
    /// </summary>
    public static byte[]? Compress(byte[] original, int quality, int maxWidth)
    {
        try
        {
            using var source = SKBitmap.Decode(original);
            if (source is null || source.Width < 1 || source.Height < 1)
            {
                return null;
            }

            using var scaled = source.Width > maxWidth
                ? source.Resize(new SKImageInfo(maxWidth, Math.Max(1, (int)Math.Round(source.Height * (double)maxWidth / source.Width))), SKFilterQuality.High)
                : null;
            using var image = SKImage.FromBitmap(scaled ?? source);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, Math.Clamp(quality, 1, 100));
            if (encoded is null)
            {
                return null;
            }

            var bytes = encoded.ToArray();
            if (bytes.Length >= original.Length)
            {
                return null; // not worth it: keep the original
            }

            // Make sure what was written can really be read back at the size we expect, before the original is ever removed.
            using var check = SKBitmap.Decode(bytes);
            var expected = scaled ?? source;
            return check is not null && check.Width == expected.Width && check.Height == expected.Height ? bytes : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or DllNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Compresses a picture file to <paramref name="destination"/> (written through a temp file and moved into place).
    /// Returns the new size, or null when nothing was written; the source file is not touched.
    /// </summary>
    public static long? CompressFile(string source, string destination, int quality, int maxWidth)
    {
        try
        {
            var bytes = Compress(File.ReadAllBytes(source), quality, maxWidth);
            if (bytes is null)
            {
                return null;
            }

            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, destination, overwrite: true);
            return bytes.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

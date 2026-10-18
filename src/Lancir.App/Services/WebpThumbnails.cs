using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace Lancir.App.Services;

/// <summary>
/// Reads and writes the downscaled picture copies kept on disk as WebP (about a third smaller than JPEG at the same
/// quality, with transparency, and fast to decode). WPF cannot encode or decode WebP itself, so SkiaSharp does it. If its native library
/// cannot be loaded on a machine, thumbnails are simply switched off: pictures still load from their originals.
/// </summary>
public static class WebpThumbnails
{
    /// <summary>File extension of a thumbnail.</summary>
    public const string Extension = ".webp";

    private const int Quality = 80;

    private static volatile bool _unavailable;

    /// <summary>Encodes <paramref name="image"/> as WebP at <paramref name="path"/> (written to a temp file first). False when it could not be saved.</summary>
    public static bool TrySave(BitmapSource image, string path)
    {
        if (_unavailable)
        {
            return false;
        }

        try
        {
            // Skia wants premultiplied BGRA; WPF decodes JPEG/PNG to Bgr32/Bgra32/Pbgra32, so convert only when needed.
            BitmapSource source = image.Format == PixelFormats.Pbgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
            var width = source.PixelWidth;
            var height = source.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);

            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), stride);
                using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, Quality));
                if (data is null)
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = File.Create(temp))
                {
                    data.SaveTo(stream);
                }

                File.Move(temp, path, overwrite: true);
                return true;
            }
            finally
            {
                handle.Free();
            }
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            _unavailable = true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Decodes a WebP thumbnail into a frozen bitmap, or null if it is missing, damaged or WebP is unavailable.</summary>
    public static BitmapSource? TryLoad(string path)
    {
        if (_unavailable)
        {
            return null;
        }

        try
        {
            using var data = SKData.Create(path);
            if (data is null)
            {
                return null;
            }

            using var codec = SKCodec.Create(data);
            if (codec is null)
            {
                return null;
            }

            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
            {
                return null;
            }

            // BitmapSource.Create copies the pixels, so the Skia buffer can be released right after.
            var image = BitmapSource.Create(
                info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null,
                bitmap.GetPixels(), info.Height * info.RowBytes, info.RowBytes);
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            _unavailable = true;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // The native Skia library failed to load (missing file, wrong architecture, blocked by security software).
    private static bool IsUnavailable(Exception ex) =>
        ex is DllNotFoundException or BadImageFormatException or TypeInitializationException or EntryPointNotFoundException;
}

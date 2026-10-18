using System.IO;
using System.Windows.Media.Imaging;

namespace Lancir.App.Services;

/// <summary>
/// Windows shows AVIF pictures only when Microsoft's "AVIF Image Extension" is installed. This tells whether a
/// downloaded file is an AVIF that this PC cannot display, so the app can point the user to the extension.
/// </summary>
public static class AvifSupport
{
    /// <summary>The Microsoft Store page of the (free) extension.</summary>
    public const string StoreUri = "ms-windows-store://pdp/?PFN=Microsoft.AVIFImageExtension_8wekyb3d8bbwe";

    private static bool _works;

    /// <summary>True when the file is an AVIF picture and Windows cannot decode it.</summary>
    public static bool IsUndisplayable(string path)
    {
        if (_works || !HasAvifSignature(path))
        {
            return false;
        }

        try
        {
            var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.None);
            _ = decoder.Frames[0].PixelWidth;
            _works = true;
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static bool HasAvifSignature(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[32];
            var read = stream.Read(head);
            if (read < 12 || head[4] != 'f' || head[5] != 't' || head[6] != 'y' || head[7] != 'p')
            {
                return false;
            }

            var text = System.Text.Encoding.ASCII.GetString(head[..read]);
            return text.Contains("avif", StringComparison.Ordinal) || text.Contains("avis", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }
}

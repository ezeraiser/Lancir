namespace Lancir.Core.Services;

/// <summary>
/// Cached pictures may have been converted to WebP after their path was saved (a cover stored as ".../5.jpg" becomes
/// ".../5.webp"). <see cref="Resolve"/> finds the file that exists today, so saved paths never go stale.
/// </summary>
public static class MediaPaths
{
    private static readonly string[] Extensions = [".webp", ".jpg", ".jpeg", ".png"];

    /// <summary>
    /// The path itself when that file exists, else the same name with another picture extension that exists (a cover saved as
    /// ".jpg" that became ".webp" when compressed, or a ".webp" that became ".jpg" again when its original was restored),
    /// else the path unchanged.
    /// </summary>
    public static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
        {
            return path;
        }

        foreach (var extension in Extensions)
        {
            var other = Path.ChangeExtension(path, extension);
            if (File.Exists(other))
            {
                return other;
            }
        }

        return path;
    }

    /// <summary>The kept original of a cached picture, whatever extension it had (jpg, jpeg, png), or null.</summary>
    public static string? FindKeptOriginal(string cachedFile)
    {
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png" })
        {
            var candidate = OriginalFor(Path.ChangeExtension(cachedFile, extension));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Where the original of a cached file goes when originals are kept: the same relative place under the originals folder.</summary>
    public static string OriginalFor(string cachedFile)
    {
        var cacheRoot = Path.GetFullPath(Path.Combine(AppPaths.AppDataFolder, "cache"));
        var relative = Path.GetRelativePath(cacheRoot, Path.GetFullPath(cachedFile));
        return Path.Combine(AppPaths.OriginalImagesFolder, relative);
    }

    /// <summary>Deletes a cached picture whichever form it is in now (the saved path or its WebP form), and its kept original.</summary>
    public static void DeleteAllForms(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        foreach (var candidate in new[] { path, Path.ChangeExtension(path, ImageCompressor.Extension) }.Distinct())
        {
            TryDelete(candidate);
            TryDelete(OriginalFor(candidate));
        }

        // an original kept under its original extension
        TryDelete(OriginalFor(path));
    }

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort
        }
    }
}

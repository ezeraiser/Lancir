namespace Lancir.Core.Services;

/// <summary>
/// Downloads cover images to a local cache folder so the library view doesn't re-fetch them
/// from the network on every load.
/// </summary>
public sealed class ImageCacheService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settings;

    public ImageCacheService(HttpClient httpClient, SettingsService settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    /// <summary>
    /// Downloads a cover or background exactly as the source serves it, replacing its compressed form and any kept original
    /// (used to restore originals). Returns the path of the file, named like a normal download.
    /// </summary>
    public async Task<string> DownloadOriginalAsync(
        int libraryGameId, string imageUrl, string? fileNameSuffix, CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureFoldersExist();
        var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var fileName = string.IsNullOrWhiteSpace(fileNameSuffix)
            ? $"{libraryGameId}{extension}"
            : $"{libraryGameId}_{fileNameSuffix}{extension}";
        var destinationPath = Path.Combine(AppPaths.ImageCacheFolder, fileName);

        var bytes = await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
        MediaPaths.DeleteAllForms(destinationPath);
        await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);
        return destinationPath;
    }

    public async Task<string> DownloadAndCacheAsync(
        int libraryGameId, string imageUrl, CancellationToken cancellationToken = default, string? fileNameSuffix = null)
    {
        AppPaths.EnsureFoldersExist();

        var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var fileName = string.IsNullOrWhiteSpace(fileNameSuffix)
            ? $"{libraryGameId}{extension}"
            : $"{libraryGameId}_{fileNameSuffix}{extension}";
        var destinationPath = Path.Combine(AppPaths.ImageCacheFolder, fileName);

        var bytes = await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken);

        // An older copy of this picture under another extension (or in another form) would go stale: remove it first.
        MediaPaths.DeleteAllForms(destinationPath);

        var settings = _settings.Load();
        if (settings.ImageStorage != ImageStorageMode.Original && ImageCompressor.IsCompressible(destinationPath))
        {
            var maxWidth = ImageCompressor.MaxWidthFor(destinationPath);
            var webp = ImageCompressor.Compress(bytes, settings.ImageQuality, maxWidth);
            if (webp is not null)
            {
                var compressedPath = Path.ChangeExtension(destinationPath, ImageCompressor.Extension);
                await File.WriteAllBytesAsync(compressedPath, webp, cancellationToken);
                OptimizationLog.Pictures.Record(libraryGameId, 1, bytes.Length, webp.Length);
                if (settings.ImageStorage == ImageStorageMode.CompressKeepOriginal)
                {
                    var kept = MediaPaths.OriginalFor(destinationPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                    await File.WriteAllBytesAsync(kept, bytes, cancellationToken);
                }

                return compressedPath;
            }
        }

        await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);

        return destinationPath;
    }
}

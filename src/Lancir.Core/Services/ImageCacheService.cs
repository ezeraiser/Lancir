namespace Lancir.Core.Services;

/// <summary>
/// Downloads cover images to a local cache folder so the library view doesn't re-fetch them
/// from the network on every load.
/// </summary>
public sealed class ImageCacheService
{
    private readonly HttpClient _httpClient;

    public ImageCacheService(HttpClient httpClient)
    {
        _httpClient = httpClient;
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
        await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);

        return destinationPath;
    }
}

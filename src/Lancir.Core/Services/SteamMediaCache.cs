using System.Security.Cryptography;
using System.Text;
using Lancir.Core.Models;

namespace Lancir.Core.Services;

/// <summary>
/// Keeps the media of a Steam-matched game on disk (screenshots, trailer thumbnail, and the
/// pictures and clips of the store description) so the details panel never has to go back to
/// Steam. Files are named after a hash of their address, so a refresh downloads only what changed
/// and removes what the store page no longer uses.
/// </summary>
public sealed class SteamMediaCache
{
    private const int ParallelDownloads = 3;


    private readonly HttpClient _httpClient;
    private readonly DebugLogService _log;
    private readonly SettingsService _settings;

    public SteamMediaCache(HttpClient httpClient, DebugLogService log, SettingsService settings)
    {
        _httpClient = httpClient;
        _log = log;
        _settings = settings;
    }

    public static string FolderFor(int gameId) => Path.Combine(AppPaths.SteamMediaFolder, gameId.ToString());

    /// <summary>File name a media address is stored under: 16 hex characters of its SHA-1, plus its extension.</summary>
    public static string FileNameFor(string url)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();
        var extension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        return hash + (string.IsNullOrEmpty(extension) ? ".bin" : extension);
    }

    /// <summary>The local file for a media address when it has been downloaded; null otherwise.</summary>
    public static string? LocalPathIfCached(int gameId, string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        // A picture may have been converted to WebP (at download time or later by the image optimizer).
        var path = Path.Combine(FolderFor(gameId), FileNameFor(url));
        if (File.Exists(path))
        {
            return path;
        }

        var webp = Path.ChangeExtension(path, ImageCompressor.Extension);
        return File.Exists(webp) ? webp : null;
    }

    /// <summary>Deletes everything stored for a game (when it leaves the library).</summary>
    public static void DeleteFor(int gameId)
    {
        try
        {
            var folder = FolderFor(gameId);
            var kept = Path.Combine(AppPaths.OriginalImagesFolder, "steam", gameId.ToString());
            if (Directory.Exists(kept))
            {
                Directory.Delete(kept, recursive: true);
            }

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    /// <summary>
    /// Makes the game's folder hold exactly the media of <paramref name="extra"/>: missing files are
    /// downloaded, files the store page no longer uses are deleted. A failed download is skipped
    /// (that item keeps loading from the web); it never fails the whole match.
    /// </summary>
    public async Task SyncAsync(int gameId, GameExtraDetails extra, CancellationToken cancellationToken = default)
    {
        var urls = MediaUrls(extra).Distinct().ToList();
        var folder = FolderFor(gameId);
        Directory.CreateDirectory(folder);

        // Compared by name without extension: a picture stored as ".jpg" and the same picture converted to ".webp" are the same item.
        var wanted = urls.Select(url => Path.GetFileNameWithoutExtension(FileNameFor(url))).ToHashSet();

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (!wanted.Contains(Path.GetFileNameWithoutExtension(file)))
            {
                TryDelete(file);
            }
        }

        var settings = _settings.Load();
        var compressedNow = new System.Collections.Concurrent.ConcurrentBag<(long Before, long After)>();

        using var gate = new SemaphoreSlim(ParallelDownloads);
        var downloads = urls.Select(async url =>
        {
            var destination = Path.Combine(folder, FileNameFor(url));
            if (File.Exists(destination) || File.Exists(Path.ChangeExtension(destination, ImageCompressor.Extension)))
            {
                return;
            }

            await gate.WaitAsync(cancellationToken);
            try
            {
                var temporary = destination + ".part";
                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var target = File.Create(temporary);
                    await source.CopyToAsync(target, cancellationToken);
                }

                // Pictures are stored compressed right away when that is on: the big original never lands in the cache.
                if (settings.ImageStorage != ImageStorageMode.Original && ImageCompressor.IsCompressible(destination))
                {
                    var webp = Path.ChangeExtension(destination, ImageCompressor.Extension);
                    var bytes = ImageCompressor.Compress(await File.ReadAllBytesAsync(temporary, cancellationToken), settings.ImageQuality,
                        ImageCompressor.ScreenshotMaxWidth);
                    if (bytes is not null)
                    {
                        compressedNow.Add((new FileInfo(temporary).Length, bytes.Length));
                        await File.WriteAllBytesAsync(webp, bytes, cancellationToken);
                        if (settings.ImageStorage == ImageStorageMode.CompressKeepOriginal)
                        {
                            var kept = MediaPaths.OriginalFor(destination);
                            Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                            File.Move(temporary, kept, overwrite: true);
                        }
                        else
                        {
                            File.Delete(temporary);
                        }

                        return;
                    }
                }

                File.Move(temporary, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                _log.Log($"Steam medya: indirilemedi ({url}): {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(downloads);
        if (!compressedNow.IsEmpty)
        {
            OptimizationLog.Pictures.Record(gameId, compressedNow.Count, compressedNow.Sum(c => c.Before), compressedNow.Sum(c => c.After));
        }

        _log.Log($"Steam medya: gameId={gameId} için {urls.Count} dosya eşitlendi.");
    }

    public enum RestoreKind
    {
        Restored,
        AlreadyOriginal,
        Failed,
    }

    public sealed record RestoreOutcome(RestoreKind Kind, long BytesBefore = 0, long BytesAfter = 0);

    /// <summary>
    /// Puts one cached file back to its original: a kept original is moved back; otherwise it is downloaded again. A file that is
    /// already the original is left alone (a clip is compared by size with what the server reports, without downloading it).
    /// </summary>
    public async Task<RestoreOutcome> RestoreOriginalAsync(int gameId, string url, CancellationToken cancellationToken = default)
    {
        var destination = Path.Combine(FolderFor(gameId), FileNameFor(url));
        var isPicture = ImageCompressor.IsCompressible(destination);
        var compressed = Path.ChangeExtension(destination, ImageCompressor.Extension);
        var current = File.Exists(compressed) && isPicture ? compressed : File.Exists(destination) ? destination : null;
        long before = current is null ? 0 : new FileInfo(current).Length;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            // 1) an original that was kept
            var kept = isPicture ? MediaPaths.FindKeptOriginal(destination) : MediaPaths.OriginalFor(destination);
            if (kept is not null && File.Exists(kept))
            {
                var target = isPicture ? Path.Combine(Path.GetDirectoryName(destination)!, Path.GetFileNameWithoutExtension(destination) + Path.GetExtension(kept)) : destination;
                File.Move(kept, target, overwrite: true);
                if (isPicture && File.Exists(compressed)) { File.Delete(compressed); }
                return new RestoreOutcome(RestoreKind.Restored, before, new FileInfo(target).Length);
            }

            // 2) nothing cached for this address (never downloaded), or a picture stored as it was downloaded: nothing to replace
            if (current is null || (isPicture && File.Exists(destination) && !File.Exists(compressed)))
            {
                return new RestoreOutcome(RestoreKind.AlreadyOriginal);
            }

            // 3) download it again (a clip first checks whether its size already matches the server's)
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (!isPicture && File.Exists(destination) && response.Content.Headers.ContentLength is { } length && length == before)
            {
                return new RestoreOutcome(RestoreKind.AlreadyOriginal);
            }

            var temporary = destination + ".part";
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = File.Create(temporary))
            {
                await source.CopyToAsync(file, cancellationToken);
            }

            File.Move(temporary, destination, overwrite: true);
            if (isPicture && File.Exists(compressed)) { File.Delete(compressed); }
            return new RestoreOutcome(RestoreKind.Restored, before, new FileInfo(destination).Length);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            _log.Log($"Steam medya: orijinal geri yüklenemedi ({url}): {ex.Message}");
            TryDelete(destination + ".part");
            return new RestoreOutcome(RestoreKind.Failed);
        }
    }

    /// <summary>What <see cref="DownloadOriginalsAsync"/> did.</summary>
    public sealed record OriginalsResult(int Downloaded, int AlreadyKept, int Failed, long Bytes);

    /// <summary>
    /// Downloads the original (uncompressed) files of a game's store pictures into the originals folder, for the ones not
    /// kept yet. The compressed pictures in use are not touched: the originals are for re-compressing later with another
    /// quality (see <see cref="ImageOptimizer.RecompressFromOriginalsAsync"/>). Clips are not included.
    /// </summary>
    public async Task<OriginalsResult> DownloadOriginalsAsync(
        int gameId, GameExtraDetails extra, CancellationToken cancellationToken = default)
    {
        int downloaded = 0, kept = 0, failed = 0;
        long bytes = 0;
        var folder = FolderFor(gameId);
        using var gate = new SemaphoreSlim(ParallelDownloads);

        var work = MediaUrls(extra).Distinct().Where(u => ImageCompressor.IsCompressible(FileNameFor(u))).Select(async url =>
        {
            // where this picture's original lives: the cache path it had before it was compressed, moved under the originals folder
            var target = MediaPaths.OriginalFor(Path.Combine(folder, FileNameFor(url)));
            if (File.Exists(target))
            {
                Interlocked.Increment(ref kept);
                return;
            }

            await gate.WaitAsync(cancellationToken);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + ".part";
                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var file = File.Create(temporary);
                    await source.CopyToAsync(file, cancellationToken);
                }

                bytes += new FileInfo(temporary).Length;
                File.Move(temporary, target, overwrite: true);
                Interlocked.Increment(ref downloaded);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                _log.Log($"Steam medya: orijinal indirilemedi ({url}): {ex.Message}");
                Interlocked.Increment(ref failed);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(work);
        return new OriginalsResult(downloaded, kept, failed, bytes);
    }

    /// <summary>Every address the details panel shows for a game: screenshots, trailer picture, description pictures and clips.</summary>
    public static IEnumerable<string> MediaUrls(GameExtraDetails extra)
    {
        foreach (var url in extra.ScreenshotUrls)
        {
            yield return url;
        }

        if (!string.IsNullOrWhiteSpace(extra.TrailerThumbnailUrl))
        {
            yield return extra.TrailerThumbnailUrl;
        }

        foreach (var line in (extra.RichDescription ?? "").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# !", StringComparison.Ordinal))
            {
                trimmed = trimmed[2..]; // text stored before a picture inside a heading got its own line
            }

            if (trimmed.StartsWith("!video ", StringComparison.Ordinal))
            {
                yield return trimmed[7..].Split('|')[0];
            }
            else if (trimmed.StartsWith("!image ", StringComparison.Ordinal))
            {
                yield return trimmed[7..].Trim();
            }
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}

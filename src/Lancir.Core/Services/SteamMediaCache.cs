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

    public SteamMediaCache(HttpClient httpClient, DebugLogService log)
    {
        _httpClient = httpClient;
        _log = log;
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

        var path = Path.Combine(FolderFor(gameId), FileNameFor(url));
        return File.Exists(path) ? path : null;
    }

    /// <summary>Deletes everything stored for a game (when it leaves the library).</summary>
    public static void DeleteFor(int gameId)
    {
        try
        {
            var folder = FolderFor(gameId);
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

        var wanted = urls.Select(FileNameFor).ToHashSet();

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (!wanted.Contains(Path.GetFileName(file)))
            {
                TryDelete(file);
            }
        }

        using var gate = new SemaphoreSlim(ParallelDownloads);
        var downloads = urls.Select(async url =>
        {
            var destination = Path.Combine(folder, FileNameFor(url));
            if (File.Exists(destination))
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
        _log.Log($"Steam medya: gameId={gameId} için {urls.Count} dosya eşitlendi.");
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

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Lancir.Core.Services;

namespace Lancir.App.Services;

/// <summary>
/// Keeps watched store trailers on disk so the second time is instant and offline-proof. Steam's trailers are HLS streams (a master
/// playlist, one video and one audio playlist of small mp4 pieces); the 720p video and the audio are copied piece by piece into a
/// folder together with playlists that point at the local pieces, and libVLC plays that folder like a stream. Nothing is held in
/// memory (one small piece at a time), the folders together stay under <see cref="MaxBytes"/> (the least recently used go first),
/// and a trailer is only used once it is complete (the master playlist is written last).
/// </summary>
public static class TrailerCache
{
    public const long MaxBytes = 256L * 1024 * 1024;
    private const int MaxHeight = 720;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly HashSet<string> Running = [];
    private static readonly object Gate = new();

    public static string Folder { get; set; } = Path.Combine(AppPaths.AppDataFolder, "cache", "trailers");

    /// <summary>True for a stream address this cache can keep.</summary>
    public static bool IsCacheable(string? source) =>
        source is not null && Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);

    /// <summary>The local master playlist of a finished copy, or null.</summary>
    public static string? TryGetLocal(string source)
    {
        var master = Path.Combine(Folder, KeyFor(source), "master.m3u8");
        if (!File.Exists(master))
        {
            return null;
        }

        try
        {
            File.SetLastWriteTimeUtc(master, DateTime.UtcNow); // recently used
        }
        catch (IOException)
        {
        }

        return master;
    }

    /// <summary>Copies the trailer to disk in the background (once at a time per trailer); failures are silent, it just stays a stream.</summary>
    public static void CacheInBackground(string source)
    {
        var key = KeyFor(source);
        lock (Gate)
        {
            if (!Running.Add(key) || File.Exists(Path.Combine(Folder, key, "master.m3u8")))
            {
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            var temp = Path.Combine(Folder, key + ".tmp");
            try
            {
                TryDelete(temp);
                Directory.CreateDirectory(temp);
                await DownloadAsync(new Uri(source), temp);
                var done = Path.Combine(Folder, key);
                TryDelete(done);
                Directory.Move(temp, done);
                Prune(key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException
                                           or InvalidDataException or UriFormatException)
            {
                System.Diagnostics.Debug.WriteLine($"TrailerCache: {ex.Message}");
                TryDelete(temp);
            }
            finally
            {
                lock (Gate)
                {
                    Running.Remove(key);
                }
            }
        });
    }

    private static async Task DownloadAsync(Uri masterUri, string folder)
    {
        var master = (await Http.GetStringAsync(masterUri)).Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        // the best video level up to 720p (or the smallest one when all are bigger), and the audio playlist
        string? variantInfo = null, variantUri = null, audioLine = null;
        var bestHeight = -1;
        var smallestHeight = int.MaxValue;
        string? smallestInfo = null, smallestUri = null;
        for (var i = 0; i < master.Count; i++)
        {
            var line = master[i];
            if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal) && line.Contains("TYPE=AUDIO"))
            {
                audioLine ??= line;
            }
            else if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal) && i + 1 < master.Count)
            {
                var height = ResolutionHeight(line);
                if (height <= MaxHeight && height > bestHeight)
                {
                    (bestHeight, variantInfo, variantUri) = (height, line, master[i + 1].Trim());
                }

                if (height < smallestHeight)
                {
                    (smallestHeight, smallestInfo, smallestUri) = (height, line, master[i + 1].Trim());
                }
            }
        }

        if (variantUri is null)
        {
            (variantInfo, variantUri) = (smallestInfo, smallestUri);
        }

        if (variantUri is null)
        {
            throw new InvalidDataException("no video level in the playlist");
        }

        var localMaster = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:7\n");
        if (audioLine is not null)
        {
            var audioUri = AttributeValue(audioLine, "URI") ?? throw new InvalidDataException("audio without address");
            await CopyPlaylistAsync(new Uri(masterUri, audioUri), folder, "a");
            localMaster.Append(audioLine.Replace($"URI=\"{audioUri}\"", "URI=\"a/a.m3u8\"")).Append('\n');
        }

        await CopyPlaylistAsync(new Uri(masterUri, variantUri), folder, "v");
        localMaster.Append(variantInfo).Append("\nv/v.m3u8\n");

        // written last: its existence means the copy is complete
        await File.WriteAllTextAsync(Path.Combine(folder, "master.m3u8"), localMaster.ToString());
    }

    /// <summary>Copies one media playlist and every piece it lists into folder/sub, one piece at a time.</summary>
    private static async Task CopyPlaylistAsync(Uri playlistUri, string folder, string sub)
    {
        var dir = Path.Combine(folder, sub);
        Directory.CreateDirectory(dir);
        var lines = (await Http.GetStringAsync(playlistUri)).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var output = new StringBuilder();
        var n = 0;
        foreach (var line in lines)
        {
            var rewritten = line;
            if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal) && AttributeValue(line, "URI") is { } map)
            {
                var local = "init" + Path.GetExtension(new Uri(playlistUri, map).AbsolutePath);
                rewritten = line.Replace($"URI=\"{map}\"", $"URI=\"{local}\"");
                await DownloadFileAsync(new Uri(playlistUri, map), Path.Combine(dir, local));
            }
            else if (line.Length > 0 && line[0] != '#')
            {
                var local = $"s{++n:D5}" + Path.GetExtension(new Uri(playlistUri, line).AbsolutePath);
                await DownloadFileAsync(new Uri(playlistUri, line), Path.Combine(dir, local));
                rewritten = local;
            }

            output.Append(rewritten).Append('\n');
        }

        if (!lines.Any(l => l.StartsWith("#EXT-X-ENDLIST", StringComparison.Ordinal)))
        {
            output.Append("#EXT-X-ENDLIST\n"); // a finished video, not a live stream
        }

        await File.WriteAllTextAsync(Path.Combine(dir, sub + ".m3u8"), output.ToString());
    }

    private static async Task DownloadFileAsync(Uri uri, string path)
    {
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024);
        await source.CopyToAsync(file, 16 * 1024);
    }

    private static int ResolutionHeight(string streamInfo)
    {
        var resolution = AttributeValue(streamInfo, "RESOLUTION");
        return resolution?.Split('x') is [_, var h] && int.TryParse(h, out var height) ? height : int.MaxValue;
    }

    private static string? AttributeValue(string line, string name)
    {
        var start = line.IndexOf(name + "=", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += name.Length + 1;
        if (start < line.Length && line[start] == '"')
        {
            var end = line.IndexOf('"', start + 1);
            return end < 0 ? null : line[(start + 1)..end];
        }

        var comma = line.IndexOf(',', start);
        return comma < 0 ? line[start..] : line[start..comma];
    }

    /// <summary>The address without its query (the expiring token): the same trailer is the same folder every time.</summary>
    private static string KeyFor(string source)
    {
        var uri = new Uri(source);
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(uri.GetLeftPart(UriPartial.Path))))[..20];
    }

    /// <summary>Deletes the least recently used trailers until the rest fits in <see cref="MaxBytes"/> (the one just saved stays).</summary>
    private static void Prune(string keep)
    {
        var dirs = new DirectoryInfo(Folder).EnumerateDirectories()
            .Where(d => !d.Name.EndsWith(".tmp", StringComparison.Ordinal))
            .Select(d =>
            {
                var master = Path.Combine(d.FullName, "master.m3u8");
                return (Dir: d, Size: d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length),
                        Used: File.Exists(master) ? File.GetLastWriteTimeUtc(master) : DateTime.MinValue);
            })
            .OrderByDescending(x => x.Used).ToList();
        var total = 0L;
        foreach (var (dir, size, _) in dirs)
        {
            total += size;
            if (total > MaxBytes && dir.Name != keep)
            {
                TryDelete(dir.FullName);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

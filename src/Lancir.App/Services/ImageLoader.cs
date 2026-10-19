using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Lancir.Core.Services;

namespace Lancir.App.Services;

/// <summary>
/// Decodes pictures off the UI thread and keeps recently shown ones in a small, size-limited memory cache.
/// Memory is the constraint (the app stays open all day): decoded pixels are capped at <see cref="BudgetBytes"/> in
/// total (least recently used pictures go first), every picture is decoded no wider than the size it is shown at,
/// at most <see cref="MaxConcurrentDecodes"/> decodes run at once so a burst never spikes memory, and the cache is
/// emptied when the window goes to the background (see <see cref="MemoryTrimmer"/>).
/// </summary>
public static class ImageLoader
{
    /// <summary>Upper bound for the decoded pixels kept for reuse (pictures currently on screen are held by their controls anyway).</summary>
    public const long MaxBudgetBytes = 256L * 1024 * 1024;

    /// <summary>Set from the memory limit in Settings (see <see cref="MemoryTrimmer"/>): at most 256 MB, and never more than 60% of the limit.</summary>
    public static long BudgetBytes { get; set; } = MaxBudgetBytes;

    private const int MaxConcurrentDecodes = 2;

    private static readonly SemaphoreSlim DecodeGate = new(MaxConcurrentDecodes);
    private static readonly object Sync = new();
    private static readonly Dictionary<string, LinkedListNode<Entry>> Entries = [];
    private static readonly LinkedList<Entry> Recent = new(); // first = most recently used
    // The newest picture of each slot (source + width), readable without taking Sync: the UI thread asks for it for every picture while a
    // page is built, and must never wait for a background thread that happens to hold the lock.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapSource> Latest = new();
    private static readonly Dictionary<string, Task<BitmapSource?>> InFlight = [];
    // A game with 190 achievements asks for 190 icons at once; the CDN drops many of such a burst, and a dropped one stayed blank.
    private static readonly SemaphoreSlim DownloadGate = new(6);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static long _bytes;

    private sealed record Entry(string Key, BitmapSource Image, long Bytes);

    /// <summary>Decoded bytes currently held by the cache (for diagnostics and tests).</summary>
    public static long CachedBytes
    {
        get
        {
            lock (Sync)
            {
                return _bytes;
            }
        }
    }

    /// <summary>True for web addresses; everything else is treated as a local file path.</summary>
    public static bool IsRemote(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The last picture decoded for this source and width, if it is still cached. No file access, so it is safe on the
    /// UI thread: the control shows it at once and <see cref="LoadAsync"/> then confirms (or replaces) it.
    /// </summary>
    public static BitmapSource? TryGetCached(string source, int width) =>
        Latest.TryGetValue(Slot(source, width), out var image) ? image : null;

    /// <summary>
    /// Loads a picture without touching the UI thread. Local files are re-checked by modification time (a cover that
    /// was re-downloaded under the same name is decoded again); web pictures are keyed by address and, with
    /// <paramref name="diskCacheFolder"/>, kept on disk so they are downloaded only once. Returns null when it cannot be shown.
    /// </summary>
    public static Task<BitmapSource?> LoadAsync(string source, int width, string? diskCacheFolder = null)
    {
        return Task.Run(async () =>
        {
            var remote = IsRemote(source);
            string stamp;
            if (remote)
            {
                stamp = "";
            }
            else
            {
                try
                {
                    var info = new FileInfo(source);
                    if (!info.Exists)
                    {
                        return null;
                    }

                    stamp = info.LastWriteTimeUtc.Ticks.ToString();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    return null;
                }
            }

            var key = $"{source}|{width}|{stamp}";
            var thumb = remote ? null : ThumbPathFor(key);
            Task<BitmapSource?> work;
            TaskCompletionSource<BitmapSource?>? owner = null;
            lock (Sync)
            {
                if (Entries.TryGetValue(key, out var hit))
                {
                    Latest[Slot(source, width)] = hit.Value.Image;
                    return Touch(hit).Image;
                }

                if (!InFlight.TryGetValue(key, out work!))
                {
                    // Only the bookkeeping happens under the lock. The work itself (file checks, starting a download, the decode) is
                    // started below, outside it: run inside the lock, its first steps blocked everybody who needed the cache, the UI
                    // thread included, for as long as a file or the network took to answer.
                    owner = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    work = owner.Task;
                    InFlight[key] = work;
                }
            }

            try
            {
                if (owner is not null)
                {
                    try
                    {
                        owner.SetResult(await DecodeAndStoreAsync(source, width, key, remote, diskCacheFolder, thumb));
                    }
                    catch (Exception ex)
                    {
                        owner.SetException(ex); // the others waiting for the same picture are told as well
                    }
                }

                return await work;
            }
            finally
            {
                lock (Sync)
                {
                    InFlight.Remove(key);
                }
            }
        });
    }

    /// <summary>
    /// Decodes a local picture right now, on the calling thread (for the few places that bind through a converter, such as
    /// dialogs). Handles WebP as well as the formats WPF reads itself. Null when it cannot be shown.
    /// </summary>
    public static BitmapSource? DecodeFileNow(string path, int width) => File.Exists(path) ? Decode(path, null, width, out _) : null;

    /// <summary>Empties the cache; pictures that are on screen stay alive through their controls.</summary>
    public static void Clear()
    {
        lock (Sync)
        {
            Entries.Clear();
            Recent.Clear();
            Latest.Clear();
            _bytes = 0;
        }
    }

    private static async Task<BitmapSource?> DecodeAndStoreAsync(
        string source, int width, string key, bool remote, string? diskCacheFolder, string? thumbPath)
    {
        try
        {
            byte[]? bytes = null;
            string? path = remote ? null : source;
            var useThumb = thumbPath is not null && File.Exists(thumbPath);
            if (useThumb)
            {
                path = thumbPath; // a downscaled copy made earlier: far less to read and decode than the original
            }

            if (remote)
            {
                (path, bytes) = await FetchRemoteAsync(source, diskCacheFolder);
                if (path is null && bytes is null)
                {
                    return null;
                }
            }

            await DecodeGate.WaitAsync();
            BitmapSource? image;
            try
            {
                var originalWidth = 0;
                image = useThumb ? WebpThumbnails.TryLoad(path!) : Decode(path, bytes, width, out originalWidth);
                if (image is null && useThumb)
                {
                    TryDelete(thumbPath!); // a damaged thumbnail: fall back to the original next time
                }
                else if (image is not null && !remote && !useThumb && thumbPath is not null && width > 0 && originalWidth > width)
                {
                    SaveThumbnailInBackground(image, thumbPath);
                }
            }
            finally
            {
                DecodeGate.Release();
            }

            if (image is null)
            {
                return null;
            }

            var cost = Math.Max(1L, (long)image.PixelWidth * image.PixelHeight * Math.Max(1, image.Format.BitsPerPixel / 8));
            lock (Sync)
            {
                Add(new Entry(key, image, cost));
                Latest[Slot(source, width)] = image;
            }

            return image;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException
                                       or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A web picture: its bytes, or the path of the copy kept on disk when a cache folder is given.</summary>
    private static async Task<(string? Path, byte[]? Bytes)> FetchRemoteAsync(string url, string? diskCacheFolder)
    {
        string? file = null;
        if (diskCacheFolder is not null)
        {
            file = Path.Combine(diskCacheFolder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
            if (File.Exists(file))
            {
                return (file, null);
            }
        }

        byte[] bytes;
        await DownloadGate.WaitAsync();
        try
        {
            try
            {
                bytes = await Http.GetByteArrayAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(700);
                bytes = await Http.GetByteArrayAsync(url); // one more try before giving up
            }
        }
        finally
        {
            DownloadGate.Release();
        }

        if (file is null)
        {
            return (null, bytes);
        }

        try
        {
            Directory.CreateDirectory(diskCacheFolder!);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes);
            File.Move(temp, file, overwrite: true);
            return (file, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, bytes); // not being able to write the disk copy only means downloading again next time
        }
    }

    private static BitmapSource? Decode(string? path, byte[]? bytes, int width, out int originalWidth)
    {
        originalWidth = 0;
        if (path is not null && bytes is null && path.EndsWith(WebpThumbnails.Extension, StringComparison.OrdinalIgnoreCase))
        {
            // WPF cannot read WebP: pictures converted to WebP (screenshots, covers, backgrounds) are decoded through Skia.
            return WebpThumbnails.TryLoad(path, width, out originalWidth);
        }

        try
        {
            if (bytes is null && width > 0)
            {
                // Header only: a picture smaller than the target must not be enlarged (that would just waste memory).
                // Read through a stream that is closed right after: a decoder made from the path keeps the file open until the
                // garbage collector gets to it, and an open file cannot be replaced or deleted (a re-downloaded cover, the optimizer).
                using (var header = File.OpenRead(path!))
                {
                    var decoder = BitmapDecoder.Create(header,
                        BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                    originalWidth = decoder.Frames[0].PixelWidth;
                }

                if (originalWidth <= width)
                {
                    width = 0;
                }
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;

            // WPF keeps its own cache of decoded pictures keyed by path: a cover re-downloaded under the same path would come back as
            // the OLD picture. This loader has its own cache that checks the file's modification time, so skip WPF's.
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (bytes is not null)
            {
                image.StreamSource = new MemoryStream(bytes, writable: false);
            }
            else
            {
                image.UriSource = new Uri(path!, UriKind.Absolute);
            }

            if (width > 0)
            {
                image.DecodePixelWidth = width; // never decode bigger than it is shown: this is what keeps memory low
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException or FileFormatException
                                       or UriFormatException or ArgumentException)
        {
            return null; // e.g. AVIF without the Windows extension, or a damaged file
        }
    }

    private static string ThumbPathFor(string key) =>
        Path.Combine(AppPaths.ThumbnailFolder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))) + WebpThumbnails.Extension);

    /// <summary>
    /// Writes the downscaled picture to disk as WebP on a background task, so the next time (even after a restart) a small
    /// file is decoded instead of the big original.
    /// </summary>
    private static void SaveThumbnailInBackground(BitmapSource image, string thumbPath) =>
        _ = Task.Run(() => WebpThumbnails.TrySave(image, thumbPath));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort
        }
    }

    /// <summary>
    /// Keeps the thumbnail folder from growing without limit: when it passes <paramref name="maxBytes"/>, the least recently
    /// used files are deleted until it is back under three quarters of that. Run once in the background at startup.
    /// </summary>
    public static void PruneThumbnails(long maxBytes = 256L * 1024 * 1024)
    {
        try
        {
            var folder = AppPaths.ThumbnailFolder;
            if (!Directory.Exists(folder))
            {
                return;
            }

            var files = new DirectoryInfo(folder).GetFiles("*" + WebpThumbnails.Extension)
                .OrderBy(f => f.LastAccessTimeUtc > f.LastWriteTimeUtc ? f.LastAccessTimeUtc : f.LastWriteTimeUtc)
                .ToList();
            var total = files.Sum(f => f.Length);
            if (total <= maxBytes)
            {
                return;
            }

            foreach (var file in files)
            {
                if (total <= maxBytes * 3 / 4)
                {
                    break;
                }

                total -= file.Length;
                TryDelete(file.FullName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort
        }
    }

    private static string Slot(string source, int width) => $"{source}|{width}";

    private static Entry Touch(LinkedListNode<Entry> node)
    {
        Recent.Remove(node);
        Recent.AddFirst(node);
        return node.Value;
    }

    private static void Add(Entry entry)
    {
        if (Entries.Remove(entry.Key, out var old))
        {
            Recent.Remove(old);
            _bytes -= old.Value.Bytes;
        }

        Entries[entry.Key] = Recent.AddFirst(entry);
        _bytes += entry.Bytes;

        // Least recently used go first; the newest picture always stays, even if it alone is over budget.
        while (_bytes > BudgetBytes && Recent.Count > 1)
        {
            var last = Recent.Last!;
            Recent.RemoveLast();
            Entries.Remove(last.Value.Key);
            _bytes -= last.Value.Bytes;
            var slot = last.Value.Key[..last.Value.Key.LastIndexOf('|')]; // key = source|width|stamp
            if (Latest.TryGetValue(slot, out var held) && ReferenceEquals(held, last.Value.Image))
            {
                Latest.TryRemove(slot, out _);
            }
        }
    }
}

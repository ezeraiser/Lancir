namespace Lancir.Core.Services;

/// <summary>Progress of an optimizer run: files handled so far out of the total.</summary>
public sealed record OptimizeProgress(int Done, int Total);

/// <summary>What an optimizer run did.</summary>
public sealed record OptimizeResult(int Converted, int Kept, int Failed, long BytesBefore, long BytesAfter)
{
    public long BytesSaved => BytesBefore - BytesAfter;
}

/// <summary>Disk use of the picture and media caches, by kind (bytes).</summary>
public sealed record CacheUsage(long Pictures, long Clips, long Thumbnails, long Originals, long Other)
{
    public long Total => Pictures + Clips + Thumbnails + Originals + Other;
}

/// <summary>
/// Converts the pictures already in the cache (screenshots and store pictures per game, covers and backgrounds) to WebP,
/// following the "image storage" setting: not at all, converted with the original kept in a separate folder, or converted with
/// the original deleted. Safe to run repeatedly: only JPEG/PNG files are touched, a file is replaced only after its WebP was
/// written and read back, files that would not get smaller are remembered and left alone, and a failure leaves the original.
/// </summary>
public sealed class ImageOptimizer
{
    private static readonly TimeSpan PauseBetweenFiles = TimeSpan.FromMilliseconds(15);

    private readonly SettingsService _settings;
    private readonly DebugLogService _log;
    private readonly SemaphoreSlim _running = new(1, 1);

    public ImageOptimizer(SettingsService settings, DebugLogService log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>Identifies the image settings a conversion was done for: the storage mode and the quality.</summary>
    private static string SignatureOf(AppSettings settings) => $"{settings.ImageStorage}|{settings.ImageQuality}";

    /// <summary>
    /// True when pictures are to be compressed and the cache has not yet been converted for the current image settings
    /// (never converted, or the user changed the storage mode or quality). The startup run only happens in that case, so the
    /// same pictures are never examined or converted again and again.
    /// </summary>
    public bool NeedsRun
    {
        get
        {
            var settings = _settings.Load();
            return settings.ImageStorage != ImageStorageMode.Original && settings.ImageOptimizedFor != SignatureOf(settings);
        }
    }

    private static string SkipListPath => Path.Combine(AppPaths.AppDataFolder, "cache", "compress-skip.txt");

    /// <summary>True while a run is in progress.</summary>
    public bool IsRunning => _running.CurrentCount == 0;

    /// <summary>
    /// Converts what can be converted. Returns null when the setting is "original" (nothing to do) or a run is already going.
    /// Meant to be called from a background task: it reads and writes many files one after another.
    /// </summary>
    public async Task<OptimizeResult?> RunAsync(IProgress<OptimizeProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Load();
        if (settings.ImageStorage == ImageStorageMode.Original || !await _running.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            var keepOriginals = settings.ImageStorage == ImageStorageMode.CompressKeepOriginal;
            var quality = settings.ImageQuality;
            var skip = LoadSkipList();
            var candidates = FindCandidates().Where(f => !skip.Contains(SkipKey(f))).ToList();

            int converted = 0, kept = 0, failed = 0;
            long before = 0, after = 0;
            var perGame = new Dictionary<int, (int Converted, long Before, long After)>();
            for (var i = 0; i < candidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OptimizeProgress(i, candidates.Count));

                var file = candidates[i];
                var outcome = ConvertOne(file, quality, keepOriginals, out var originalBytes, out var newBytes);
                switch (outcome)
                {
                    case Outcome.Converted:
                        converted++;
                        before += originalBytes;
                        after += newBytes;
                        if (OptimizationLog.GameIdFor(file) is { } gameId)
                        {
                            perGame.TryGetValue(gameId, out var sofar);
                            perGame[gameId] = (sofar.Converted + 1, sofar.Before + originalBytes, sofar.After + newBytes);
                        }

                        break;
                    case Outcome.NotSmaller:
                        kept++;
                        skip.Add(SkipKey(file));
                        break;
                    default:
                        failed++;
                        break;
                }

                await Task.Delay(PauseBetweenFiles, cancellationToken);
            }

            SaveSkipList(skip);
            OptimizationLog.Pictures.RecordMany(perGame.Select(kv => (kv.Key, kv.Value.Converted, kv.Value.Before, kv.Value.After)));
            progress?.Report(new OptimizeProgress(candidates.Count, candidates.Count));

            // Remember that the cache is done for these settings, so the next start does not look at it again. Files that
            // could not be handled right now (in use) leave it unmarked: the next start tries just those again.
            if (failed == 0)
            {
                settings.ImageOptimizedFor = SignatureOf(settings);
                _settings.Save(settings);
            }

            var result = new OptimizeResult(converted, kept, failed, before, after);
            _log.Log($"Görsel sıkıştırma: {converted} dosya sıkıştırıldı ({before / 1048576.0:F1} MB -> {after / 1048576.0:F1} MB), " +
                     $"{kept} dosya zaten küçük, {failed} hata.");
            return result;
        }
        finally
        {
            _running.Release();
        }
    }

    /// <summary>
    /// Re-makes the compressed pictures from the originals that are kept (in the originals folder) with the current quality and size
    /// limits, so a picture can be revised after the fact (a different quality, say) without compressing a compressed picture again.
    /// Pictures whose original is not kept are left as they are. The originals stay where they are.
    /// </summary>
    public async Task<OptimizeResult?> RecompressFromOriginalsAsync(
        IProgress<OptimizeProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!await _running.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            var quality = _settings.Load().ImageQuality;
            var cacheRoot = Path.GetFullPath(Path.Combine(AppPaths.AppDataFolder, "cache"));
            var originalsRoot = Path.GetFullPath(AppPaths.OriginalImagesFolder);
            var originals = Directory.Exists(originalsRoot)
                ? Directory.EnumerateFiles(originalsRoot, "*", SearchOption.AllDirectories).Where(ImageCompressor.IsCompressible).ToList()
                : [];

            int converted = 0, failed = 0;
            long before = 0, after = 0;
            for (var i = 0; i < originals.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OptimizeProgress(i, originals.Count));

                var original = originals[i];
                // the same relative place in the cache, as WebP
                var cached = Path.ChangeExtension(Path.Combine(cacheRoot, Path.GetRelativePath(originalsRoot, original)), ImageCompressor.Extension);
                var size = ImageCompressor.CompressFile(original, cached, quality, ImageCompressor.MaxWidthFor(cached));
                if (size is { } written)
                {
                    converted++;
                    before += new FileInfo(original).Length;
                    after += written;
                }
                else
                {
                    failed++; // not smaller than its original, or unreadable: the picture in use stays
                }

                await Task.Delay(PauseBetweenFiles, cancellationToken);
            }

            progress?.Report(new OptimizeProgress(originals.Count, originals.Count));
            _log.Log($"Görsel yeniden sıkıştırma: {converted} dosya orijinalinden yeniden üretildi (kalite {quality}), {failed} dosya atlandı.");
            return new OptimizeResult(converted, 0, failed, before, after);
        }
        finally
        {
            _running.Release();
        }
    }

    /// <summary>How much disk the caches use, by kind. Walks the cache folders: call it off the UI thread.</summary>
    public static CacheUsage MeasureCache()
    {
        var cache = Path.Combine(AppPaths.AppDataFolder, "cache");
        long pictures = 0, clips = 0, thumbs = 0, originals = 0, other = 0;
        if (!Directory.Exists(cache))
        {
            return new CacheUsage(0, 0, 0, 0, 0);
        }

        foreach (var file in Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories))
        {
            long length;
            try
            {
                length = new FileInfo(file).Length;
            }
            catch (IOException)
            {
                continue;
            }

            var full = Path.GetFullPath(file);
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (full.StartsWith(Path.GetFullPath(AppPaths.OriginalImagesFolder), StringComparison.OrdinalIgnoreCase))
            {
                originals += length;
            }
            else if (full.StartsWith(Path.GetFullPath(AppPaths.ThumbnailFolder), StringComparison.OrdinalIgnoreCase))
            {
                thumbs += length;
            }
            else if (ext is ".mp4" or ".webm")
            {
                clips += length;
            }
            else if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".avif" or ".img")
            {
                pictures += length;
            }
            else
            {
                other += length;
            }
        }

        return new CacheUsage(pictures, clips, thumbs, originals, other);
    }

    private enum Outcome
    {
        Converted,
        NotSmaller,
        Failed,
    }

    private static Outcome ConvertOne(string file, int quality, bool keepOriginal, out long originalBytes, out long newBytes)
    {
        originalBytes = 0;
        newBytes = 0;
        try
        {
            originalBytes = new FileInfo(file).Length;
            var destination = Path.ChangeExtension(file, ImageCompressor.Extension);
            var written = ImageCompressor.CompressFile(file, destination, quality, ImageCompressor.MaxWidthFor(file));
            if (written is not { } size)
            {
                return File.Exists(destination) ? Outcome.Failed : Outcome.NotSmaller;
            }

            newBytes = size;
            if (keepOriginal)
            {
                var kept = MediaPaths.OriginalFor(file);
                Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                File.Move(file, kept, overwrite: true);
            }
            else
            {
                File.Delete(file);
            }

            return Outcome.Converted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use or locked: the original stays where it is and the next run tries again.
            return Outcome.Failed;
        }
    }

    private static IEnumerable<string> FindCandidates()
    {
        foreach (var folder in new[] { AppPaths.SteamMediaFolder, AppPaths.ImageCacheFolder })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (ImageCompressor.IsCompressible(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static string SkipKey(string file) =>
        Path.GetRelativePath(Path.Combine(AppPaths.AppDataFolder, "cache"), file).Replace('\\', '/') + "|" + new FileInfo(file).Length;

    private static HashSet<string> LoadSkipList()
    {
        try
        {
            return File.Exists(SkipListPath) ? [.. File.ReadAllLines(SkipListPath)] : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static void SaveSkipList(HashSet<string> skip)
    {
        try
        {
            File.WriteAllLines(SkipListPath, skip);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // only a speed-up for the next run
        }
    }
}

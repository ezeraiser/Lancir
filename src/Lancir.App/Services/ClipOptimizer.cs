using System.IO;
using Lancir.Core.Services;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Lancir.App.Services;

/// <summary>Progress of a clip optimization: clips handled out of the total, and the clip being worked on.</summary>
public sealed record ClipProgress(int Done, int Total, string? Current);

/// <summary>What a clip optimization run did.</summary>
public sealed record ClipResult(int Converted, int AlreadySmall, int Failed, long BytesBefore, long BytesAfter)
{
    public long BytesSaved => BytesBefore - BytesAfter;
}

/// <summary>
/// Re-encodes the short clips of the store descriptions (mp4) to a smaller size and bit rate with the encoder that is built
/// into Windows (Media Foundation: no extra files, uses the graphics card when it can). The planned size and bit rate come from
/// <see cref="ClipPlanner"/>. Nothing is replaced unless the new file is at least a quarter smaller and has the same length; the
/// original is deleted or moved to the originals folder according to the setting, and a clip that cannot be converted (no
/// decoder for it on this PC, a damaged file, in use by the player) is simply left as it is. Every clip is looked at once:
/// handled clips are remembered, so the same clips are never re-encoded again and again (which would only lose quality).
/// </summary>
public sealed class ClipOptimizer
{
    private static readonly TimeSpan PerClipTimeout = TimeSpan.FromMinutes(3);

    private readonly SettingsService _settings;
    private readonly DebugLogService _log;
    private readonly SemaphoreSlim _running = new(1, 1);

    public ClipOptimizer(SettingsService settings, DebugLogService log)
    {
        _settings = settings;
        _log = log;
    }

    private static string ProcessedListPath => Path.Combine(AppPaths.AppDataFolder, "cache", "clip-processed.txt");

    public bool IsRunning => _running.CurrentCount == 0;

    /// <summary>Clips that have not been handled yet (not optimized, not judged small enough, not found unconvertible).</summary>
    public IReadOnlyList<string> PendingClips()
    {
        var processed = LoadProcessed();
        return FindClips().Where(f => !processed.Contains(KeyOf(f))).ToList();
    }

    /// <summary>True when clips are to be optimized and there are clips not handled yet.</summary>
    public bool NeedsRun => _settings.Load().ClipStorage != ImageStorageMode.Original && PendingClips().Count > 0;

    /// <summary>
    /// Optimizes the pending clips. Returns null when the setting is "original" or a run is already going. Call it from a
    /// background task; it can take a few seconds per clip.
    /// </summary>
    public async Task<ClipResult?> RunAsync(IProgress<ClipProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Load();
        if (settings.ClipStorage == ImageStorageMode.Original || !await _running.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            var keepOriginals = settings.ClipStorage == ImageStorageMode.CompressKeepOriginal;
            var preset = settings.ClipQuality;
            var processed = LoadProcessed();
            var pending = FindClips().Where(f => !processed.Contains(KeyOf(f))).ToList();

            int converted = 0, small = 0, failed = 0;
            long before = 0, after = 0;
            var perGame = new Dictionary<int, (int Converted, long Before, long After)>();
            for (var i = 0; i < pending.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = pending[i];
                progress?.Report(new ClipProgress(i, pending.Count, Path.GetFileName(file)));

                var outcome = await ConvertOneAsync(file, preset, keepOriginals, cancellationToken);
                switch (outcome.Kind)
                {
                    case Kind.Converted:
                        converted++;
                        before += outcome.OriginalBytes;
                        after += outcome.NewBytes;
                        processed.Add(KeyOf(file)); // the new file is handled as well
                        if (OptimizationLog.GameIdFor(file) is { } gameId)
                        {
                            perGame.TryGetValue(gameId, out var sofar);
                            perGame[gameId] = (sofar.Converted + 1, sofar.Before + outcome.OriginalBytes, sofar.After + outcome.NewBytes);
                        }

                        break;
                    case Kind.AlreadySmall:
                        small++;
                        processed.Add(KeyOf(file));
                        break;
                    case Kind.Unconvertible:
                        failed++;
                        processed.Add(KeyOf(file)); // would fail the same way next time
                        break;
                    default: // Busy: in use right now, try again on another run
                        failed++;
                        break;
                }
            }

            SaveProcessed(processed);
            OptimizationLog.Clips.RecordMany(perGame.Select(kv => (kv.Key, kv.Value.Converted, kv.Value.Before, kv.Value.After)));
            progress?.Report(new ClipProgress(pending.Count, pending.Count, null));
            var result = new ClipResult(converted, small, failed, before, after);
            _log.Log($"Klip optimizasyonu: {converted} klip küçültüldü ({before / 1048576.0:F1} MB -> {after / 1048576.0:F1} MB), " +
                     $"{small} klip zaten küçük, {failed} klip dönüştürülemedi.");
            return result;
        }
        finally
        {
            _running.Release();
        }
    }

    private enum Kind
    {
        Converted,
        AlreadySmall,
        Unconvertible,
        Busy,
    }

    private sealed record Outcome(Kind Kind, long OriginalBytes = 0, long NewBytes = 0);

    private async Task<Outcome> ConvertOneAsync(string file, ClipQualityPreset preset, bool keepOriginal, CancellationToken cancellationToken)
    {
        var temp = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + ".opt.mp4");
        var planned = ""; // what was asked of the encoder, for the log when it refuses
        try
        {
            var originalBytes = new FileInfo(file).Length;
            var source = await StorageFile.GetFileFromPathAsync(file);
            var sourceProfile = await MediaEncodingProfile.CreateFromFileAsync(source);
            var video = sourceProfile?.Video;
            var duration = (await source.Properties.GetVideoPropertiesAsync()).Duration;
            if (video is null || duration <= TimeSpan.Zero)
            {
                return new Outcome(Kind.Unconvertible);
            }

            var bitrate = (long)(originalBytes * 8 / duration.TotalSeconds);
            if (ClipPlanner.Plan((int)video.Width, (int)video.Height, bitrate, preset) is not { } target)
            {
                return new Outcome(Kind.AlreadySmall);
            }

            planned = $"{video.Width}x{video.Height} -> {target.Width}x{target.Height} @{target.Bitrate / 1000} kbps";
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(file)!);
            var destination = await folder.CreateFileAsync(Path.GetFileName(temp), CreationCollisionOption.ReplaceExisting);

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
            profile.Audio = null; // these clips have no sound
            profile.Video.Width = (uint)target.Width;
            profile.Video.Height = (uint)target.Height;
            profile.Video.Bitrate = (uint)target.Bitrate;
            var fps = video.FrameRate.Denominator > 0 ? (double)video.FrameRate.Numerator / video.FrameRate.Denominator : 30;
            profile.Video.FrameRate.Numerator = (uint)Math.Round(Math.Clamp(fps, 10, 30));
            profile.Video.FrameRate.Denominator = 1;

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await transcoder.PrepareFileTranscodeAsync(source, destination, profile);
            if (!prepared.CanTranscode)
            {
                return new Outcome(Kind.Unconvertible);
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(PerClipTimeout);
                await prepared.TranscodeAsync().AsTask(timeout.Token);
            }

            // Only a result that is clearly smaller and has the same length replaces the original.
            var newBytes = new FileInfo(temp).Length;
            var newDuration = (await destination.Properties.GetVideoPropertiesAsync()).Duration;
            var sameLength = Math.Abs((newDuration - duration).TotalSeconds) <= Math.Max(0.5, duration.TotalSeconds * 0.1);
            if (!sameLength)
            {
                return new Outcome(Kind.Unconvertible);
            }

            if (!ClipPlanner.IsWorthKeeping(originalBytes, newBytes))
            {
                return new Outcome(Kind.AlreadySmall);
            }

            if (keepOriginal)
            {
                var kept = MediaPaths.OriginalFor(file);
                Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                File.Move(file, kept, overwrite: true);
                File.Move(temp, file);
            }
            else
            {
                File.Move(temp, file, overwrite: true);
            }

            return new Outcome(Kind.Converted, originalBytes, newBytes);
        }
        catch (IOException)
        {
            return new Outcome(Kind.Busy); // the clip is playing or otherwise open: another run will get it
        }
        catch (UnauthorizedAccessException)
        {
            return new Outcome(Kind.Busy);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Outcome(Kind.Unconvertible); // took too long
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            // Windows could not read or write this clip (e.g. no HEVC decoder installed, or a damaged file).
            _log.Log($"Klip optimizasyonu: {Path.GetFileName(file)} dönüştürülemedi ({ex.Message.Trim()}) [{planned}].");
            return new Outcome(Kind.Unconvertible);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    // Only the downloaded mp4 clips; webm needs a Windows extension to be read, so it is left alone.
    private static IEnumerable<string> FindClips()
    {
        var folder = AppPaths.SteamMediaFolder;
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.mp4", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".opt.mp4", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static string KeyOf(string file) =>
        Path.GetRelativePath(Path.Combine(AppPaths.AppDataFolder, "cache"), file).Replace('\\', '/') + "|" + new FileInfo(file).Length;

    private static HashSet<string> LoadProcessed()
    {
        try
        {
            return File.Exists(ProcessedListPath) ? [.. File.ReadAllLines(ProcessedListPath)] : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static void SaveProcessed(HashSet<string> processed)
    {
        try
        {
            File.WriteAllLines(ProcessedListPath, processed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // only a speed-up: without it the next run looks at the same clips again
        }
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

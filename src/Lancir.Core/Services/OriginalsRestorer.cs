using Lancir.Core.Data;
using Lancir.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lancir.Core.Services;

/// <summary>Progress of a restore: games handled out of the total, and the game being worked on.</summary>
public sealed record RestoreProgress(int Done, int Total, string? Current);

/// <summary>What a restore put back, in numbers.</summary>
public sealed record RestoreResult(
    int Pictures, int Clips, int Covers, int CoversWithoutSource, int Failed, long BytesBefore, long BytesAfter);

/// <summary>Raised when media files were replaced behind the app's back, so open views can reload what they show.</summary>
public static class MediaEvents
{
    public static event Action? Changed;

    public static void RaiseChanged() => Changed?.Invoke();
}

/// <summary>
/// Puts the original files back in place of everything compression changed: the screenshots and store pictures, the clips that
/// were re-encoded, and the covers and backgrounds. For each file the kept original (originals folder) is moved back when there is
/// one, otherwise it is downloaded again from the address it came from. What is already original is left alone (a clip is checked
/// by size against the server, without downloading it). A cover or background whose source address was not recorded (those
/// downloaded before addresses were remembered) cannot be fetched again and is only counted: re-matching the game gets it.
/// </summary>
public sealed class OriginalsRestorer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DebugLogService _log;
    private readonly SemaphoreSlim _running = new(1, 1);

    public OriginalsRestorer(IServiceScopeFactory scopeFactory, DebugLogService log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public bool IsRunning => _running.CurrentCount == 0;

    /// <summary>Restores everything for every game. Returns null when a restore is already running.</summary>
    public async Task<RestoreResult?> RunAsync(IProgress<RestoreProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!await _running.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var media = scope.ServiceProvider.GetRequiredService<SteamMediaCache>();
            var covers = scope.ServiceProvider.GetRequiredService<ImageCacheService>();

            var games = await db.Games.ToListAsync(cancellationToken);
            var clipGames = OptimizationLog.Clips.Load().Select(e => e.GameId).ToHashSet();

            int pictures = 0, clips = 0, restoredCovers = 0, noSource = 0, failed = 0;
            long before = 0, after = 0;
            for (var i = 0; i < games.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var game = games[i];
                progress?.Report(new RestoreProgress(i, games.Count, game.DisplayName));

                // the store pictures and clips of the details page
                var extra = new GameExtraDetails
                {
                    RichDescription = game.DescriptionRich,
                    TrailerThumbnailUrl = game.TrailerThumbnailUrl,
                    ScreenshotUrls = (game.ScreenshotUrls ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                };
                foreach (var url in SteamMediaCache.MediaUrls(extra).Distinct())
                {
                    var isPicture = ImageCompressor.IsCompressible(SteamMediaCache.FileNameFor(url));
                    var isClip = SteamMediaCache.FileNameFor(url).EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
                    if (!isPicture && !(isClip && (clipGames.Contains(game.Id) || KeptClipExists(game.Id, url))))
                    {
                        continue; // other clips (webm) were never touched
                    }

                    var outcome = await media.RestoreOriginalAsync(game.Id, url, cancellationToken);
                    switch (outcome.Kind)
                    {
                        case SteamMediaCache.RestoreKind.Restored:
                            if (isClip) { clips++; } else { pictures++; }
                            before += outcome.BytesBefore;
                            after += outcome.BytesAfter;
                            break;
                        case SteamMediaCache.RestoreKind.Failed:
                            failed++;
                            break;
                    }
                }

                // cover and background
                foreach (var (isBackground, path, url) in new[]
                         {
                             (false, game.CoverImagePath, game.CoverImageUrl),
                             (true, game.BackgroundImagePath, game.BackgroundImageUrl),
                         })
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    var resolved = MediaPaths.Resolve(path)!;
                    if (!resolved.EndsWith(ImageCompressor.Extension, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // already the original
                    }

                    var restored = await RestoreCoverAsync(covers, game.Id, resolved, url, isBackground, cancellationToken);
                    if (restored.Path is null)
                    {
                        if (restored.NoSource) { noSource++; } else { failed++; }
                        continue;
                    }

                    restoredCovers++;
                    before += restored.BytesBefore;
                    after += restored.BytesAfter;
                    if (isBackground) { game.BackgroundImagePath = restored.Path; } else { game.CoverImagePath = restored.Path; }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            progress?.Report(new RestoreProgress(games.Count, games.Count, null));
            _log.Log($"Orijinalleri geri yükleme: {pictures} görsel, {clips} klip, {restoredCovers} kapak/arka plan geri yüklendi; " +
                     $"{noSource} kapak/arka planın kaynağı kayıtlı değil; {failed} hata.");
            MediaEvents.RaiseChanged();
            return new RestoreResult(pictures, clips, restoredCovers, noSource, failed, before, after);
        }
        finally
        {
            _running.Release();
        }
    }

    private static bool KeptClipExists(int gameId, string url) =>
        File.Exists(MediaPaths.OriginalFor(Path.Combine(SteamMediaCache.FolderFor(gameId), SteamMediaCache.FileNameFor(url))));

    private sealed record CoverOutcome(string? Path, bool NoSource, long BytesBefore, long BytesAfter);

    private static async Task<CoverOutcome> RestoreCoverAsync(
        ImageCacheService covers, int gameId, string compressedPath, string? url, bool isBackground, CancellationToken cancellationToken)
    {
        try
        {
            var before = new FileInfo(compressedPath).Length;

            // 1) an original that was kept: move it back next to where the compressed one is, with its own extension
            if (MediaPaths.FindKeptOriginal(compressedPath) is { } kept)
            {
                var destination = Path.Combine(Path.GetDirectoryName(compressedPath)!,
                    Path.GetFileNameWithoutExtension(compressedPath) + Path.GetExtension(kept));
                File.Move(kept, destination, overwrite: true);
                File.Delete(compressedPath);
                return new CoverOutcome(destination, false, before, new FileInfo(destination).Length);
            }

            // 2) download it again from the recorded address
            if (string.IsNullOrWhiteSpace(url))
            {
                return new CoverOutcome(null, NoSource: true, 0, 0);
            }

            var restored = await covers.DownloadOriginalAsync(gameId, url, isBackground ? "background" : null, cancellationToken);
            return new CoverOutcome(restored, false, before, new FileInfo(restored).Length);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            return new CoverOutcome(null, NoSource: false, 0, 0);
        }
    }
}

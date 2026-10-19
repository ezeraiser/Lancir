using Lancir.Core.Data;
using Lancir.Core.Models;
using Lancir.Core.Services;
using Lancir.Core.Steam;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lancir.Tests.Services;

/// <summary>
/// PlaytimeTracker writes playtime through a context of its own, so the games the library page tracks can hold older values. A Steam
/// import must add Steam's time to the newest value, not to the stale one (that would take tracked time away).
/// </summary>
public sealed class SteamPlaytimeRefreshTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<LibraryDbContext> _options;
    private readonly int _gameId;

    public SteamPlaytimeRefreshTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(_connection).Options;
        using var db = new LibraryDbContext(_options);
        db.Database.EnsureCreated();
        var game = new LibraryGame { FolderPath = "C:/g", DisplayName = "Game", SteamAppId = 7, PlaytimeSeconds = 100 };
        db.Games.Add(game);
        db.SaveChanges();
        _gameId = game.Id;
    }

    private static LibraryService ServiceFor(LibraryDbContext db) =>
        new(db, null!, null!, null!, null!, null!, null!, null!, null!, new DebugLogService());

    private static SteamImportRow Row(int gameId, long steamSeconds) =>
        new(new SteamOwnedGame(7, "Game", steamSeconds, null), SteamImportState.InLibrary, gameId);

    private (long Playtime, long Offset) Stored()
    {
        using var db = new LibraryDbContext(_options);
        var game = db.Games.Single(g => g.Id == _gameId);
        return (game.PlaytimeSeconds, game.SteamPlaytimeOffsetSeconds);
    }

    [Fact]
    public async Task TimeTheTrackerCountedMeanwhileIsNotTakenAwayBySteam()
    {
        using var library = new LibraryDbContext(_options);     // the library page: tracks the game with 100 s
        var service = ServiceFor(library);
        await service.GetLibraryAsync();

        using (var tracker = new LibraryDbContext(_options))     // the tracker has counted more since, in its own context
        {
            tracker.Games.Single(g => g.Id == _gameId).PlaytimeSeconds = 500;
            tracker.SaveChanges();
        }

        await service.ApplySteamImportAsync([Row(_gameId, steamSeconds: 300)]); // Steam knows less than Lancir does

        Assert.Equal((500L, 0L), Stored());                      // with the stale 100 this would have become 300
    }

    [Fact]
    public async Task SteamTimeLancirDidNotSeeIsAddedToTheFreshValue()
    {
        using var library = new LibraryDbContext(_options);
        var service = ServiceFor(library);
        await service.GetLibraryAsync();

        using (var tracker = new LibraryDbContext(_options))
        {
            tracker.Games.Single(g => g.Id == _gameId).PlaytimeSeconds = 500;
            tracker.SaveChanges();
        }

        await service.ApplySteamImportAsync([Row(_gameId, steamSeconds: 600)]);

        Assert.Equal((600L, 100L), Stored());                    // 500 counted by Lancir + 100 only Steam knew
    }

    [Fact]
    public async Task ImportingAgainChangesNothing()
    {
        using var library = new LibraryDbContext(_options);
        var service = ServiceFor(library);
        await service.GetLibraryAsync();

        await service.ApplySteamImportAsync([Row(_gameId, steamSeconds: 600)]);
        var first = Stored();
        await service.ApplySteamImportAsync([Row(_gameId, steamSeconds: 600)]);

        Assert.Equal(first, Stored());
    }

    public void Dispose() => _connection.Dispose();
}

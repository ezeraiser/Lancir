using Lancir.Core.Data;
using Lancir.Core.Models;
using Lancir.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lancir.Tests.Services;

public sealed class CustomOrderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<LibraryDbContext> _options;

    public CustomOrderTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(_connection).Options;
        using var db = new LibraryDbContext(_options);
        db.Database.EnsureCreated();
        for (var i = 1; i <= 5; i++)
        {
            db.Games.Add(new LibraryGame { FolderPath = $"C:/g{i}", DisplayName = $"Game {i}", SortOrder = i });
        }

        db.SaveChanges();
    }

    private static LibraryService ServiceFor(LibraryDbContext db) => new(db, null!, null!, null!, null!, null!, null!, null!, null!, null!);

    private Dictionary<string, int> Orders()
    {
        using var db = new LibraryDbContext(_options);
        return db.Games.ToDictionary(g => g.DisplayName, g => g.SortOrder);
    }

    private List<int> Ids()
    {
        using var db = new LibraryDbContext(_options);
        return db.Games.OrderBy(g => g.Id).Select(g => g.Id).ToList();
    }

    [Fact]
    public async Task SavesTheNewOrderOfGamesTheLibraryPageAlreadyLoaded()
    {
        var ids = Ids();
        using var db = new LibraryDbContext(_options);
        var service = ServiceFor(db);
        await service.GetLibraryAsync(); // what the library page does: the games are tracked from here on

        await service.SaveCustomOrderAsync([ids[2], ids[0], ids[1], ids[3], ids[4]]);

        var orders = Orders();
        Assert.Equal(1, orders["Game 3"]);
        Assert.Equal(2, orders["Game 1"]);
        Assert.Equal(3, orders["Game 2"]);
        Assert.Equal(4, orders["Game 4"]);
        Assert.Equal(5, orders["Game 5"]);
    }

    [Fact]
    public async Task SavesTheOrderEvenWhenNothingWasLoadedFirst()
    {
        var ids = Ids();
        using var db = new LibraryDbContext(_options);

        await ServiceFor(db).SaveCustomOrderAsync([ids[4], ids[3], ids[2], ids[1], ids[0]]);

        var orders = Orders();
        Assert.Equal(1, orders["Game 5"]);
        Assert.Equal(5, orders["Game 1"]);
    }

    [Fact]
    public async Task GamesNotInTheListKeepTheirOrder()
    {
        var ids = Ids();
        using var db = new LibraryDbContext(_options);
        var service = ServiceFor(db);
        await service.GetLibraryAsync();

        await service.SaveCustomOrderAsync([ids[1], ids[0]]); // only two games named

        var orders = Orders();
        Assert.Equal(1, orders["Game 2"]);
        Assert.Equal(2, orders["Game 1"]);
        Assert.Equal(3, orders["Game 3"]);
        Assert.Equal(5, orders["Game 5"]);
    }

    public void Dispose() => _connection.Dispose();
}

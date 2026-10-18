using Lancir.Core.Models;
using Lancir.Core.Services;
using Lancir.Core.Steam;

namespace Lancir.Tests.Services;

public class SteamImportTests
{
    private static LibraryGame Game(string name, int? appId = null, long playtime = 0, long offset = 0, int id = 0) =>
        new()
        {
            Id = id,
            FolderPath = "",
            DisplayName = name,
            SteamAppId = appId,
            PlaytimeSeconds = playtime,
            SteamPlaytimeOffsetSeconds = offset,
        };

    // ---- playtime merge ----

    [Fact]
    public void ApplyPlaytime_AddsSteamTimeLancirDidNotSee()
    {
        var game = Game("Warframe", 230410, playtime: 600); // Lancir tracked 10 min
        SteamImport.ApplyPlaytime(game, 36000); // Steam says 10 h

        Assert.Equal(36000, game.PlaytimeSeconds);
        Assert.Equal(35400, game.SteamPlaytimeOffsetSeconds);
    }

    [Fact]
    public void ApplyPlaytime_IsIdempotent()
    {
        var game = Game("Warframe", 230410, playtime: 600);
        SteamImport.ApplyPlaytime(game, 36000);
        SteamImport.ApplyPlaytime(game, 36000);

        Assert.Equal(36000, game.PlaytimeSeconds);
        Assert.Equal(35400, game.SteamPlaytimeOffsetSeconds);
    }

    [Fact]
    public void ApplyPlaytime_TimeBothCountedIsNotDoubled()
    {
        var game = Game("Warframe", 230410, playtime: 600);
        SteamImport.ApplyPlaytime(game, 36000);

        // 30 more minutes played with Lancir open: Lancir tracks them and Steam counts them too.
        game.PlaytimeSeconds += 1800;
        SteamImport.ApplyPlaytime(game, 36000 + 1800);

        Assert.Equal(37800, game.PlaytimeSeconds);
        Assert.Equal(35400, game.SteamPlaytimeOffsetSeconds);
    }

    [Fact]
    public void ApplyPlaytime_NeverLowersPlaytime()
    {
        var game = Game("Game", 1, playtime: 7200);
        SteamImport.ApplyPlaytime(game, 3600); // Steam knows less than Lancir tracked

        Assert.Equal(7200, game.PlaytimeSeconds);
        Assert.Equal(0, game.SteamPlaytimeOffsetSeconds);
    }

    [Fact]
    public void ApplyPlaytime_ReplacesAnEarlierSteamOffset()
    {
        var game = Game("Game", 1, playtime: 3000, offset: 2400); // Lancir own = 600
        SteamImport.ApplyPlaytime(game, 4000);

        Assert.Equal(4000, game.PlaytimeSeconds);
        Assert.Equal(3400, game.SteamPlaytimeOffsetSeconds);
    }

    // ---- rows ----

    [Fact]
    public void BuildRows_ClassifiesNewLinkedAndMatchedByName()
    {
        var owned = new List<SteamOwnedGame>
        {
            new(230410, "Warframe", 100, null),
            new(570, "Dota 2", 50, null),
            new(1091500, "Cyberpunk 2077", 0, null),
        };
        var library = new List<LibraryGame>
        {
            Game("Warframe", 230410, id: 1),
            Game("DOTA 2", id: 2), // no Steam id yet, same name
        };

        var rows = SteamImport.BuildRows(owned, library);

        Assert.Equal(SteamImportState.InLibrary, rows[0].State);
        Assert.Equal(1, rows[0].ExistingGameId);
        Assert.Equal(SteamImportState.MatchedByName, rows[1].State);
        Assert.Equal(2, rows[1].ExistingGameId);
        Assert.Equal(SteamImportState.New, rows[2].State);
        Assert.Null(rows[2].ExistingGameId);
    }

    [Fact]
    public void BuildRows_OneLibraryGameIsClaimedByOnlyOneSteamGameByName()
    {
        var owned = new List<SteamOwnedGame> { new(1, "Same Name", 0, null), new(2, "Same Name", 0, null) };
        var rows = SteamImport.BuildRows(owned, [Game("Same Name", id: 7)]);

        Assert.Equal(SteamImportState.MatchedByName, rows[0].State);
        Assert.Equal(SteamImportState.New, rows[1].State);
    }

    [Theory]
    [InlineData("The Witcher® 3: Wild Hunt", "thewitcher3wildhunt")]
    [InlineData("  DOTA 2 ", "dota2")]
    [InlineData("!!!", "")]
    public void NormalizeName_KeepsLettersAndDigitsOnly(string name, string expected) =>
        Assert.Equal(expected, SteamImport.NormalizeName(name));

    // ---- Steam answers ----

    [Fact]
    public void ParseOwnedGames_ReadsNamePlaytimeAndLastPlayed()
    {
        const string json = """
            {"response":{"game_count":2,"games":[
              {"appid":230410,"name":"Warframe","playtime_forever":600,"rtime_last_played":1700000000},
              {"appid":570,"playtime_forever":0,"rtime_last_played":0}
            ]}}
            """;

        var games = SteamLibraryClient.ParseOwnedGames(json)!;

        Assert.Equal(2, games.Count);
        Assert.Equal(new SteamOwnedGame(230410, "Warframe", 36000, DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime), games[0]);
        Assert.Equal("App 570", games[1].Name);
        Assert.Null(games[1].LastPlayedUtc);
    }

    [Fact]
    public void ParseOwnedGames_NoGameCountMeansPrivate()
    {
        Assert.Null(SteamLibraryClient.ParseOwnedGames("""{"response":{}}"""));
        Assert.Null(SteamLibraryClient.ParseOwnedGames("{}"));
    }

    [Fact]
    public void ParseOwnedGames_AccountThatOwnsNothingIsEmptyNotPrivate()
    {
        var games = SteamLibraryClient.ParseOwnedGames("""{"response":{"game_count":0}}""");
        Assert.NotNull(games);
        Assert.Empty(games);
    }

    [Theory]
    [InlineData("76561197991886348", "76561197991886348", null)]
    [InlineData("https://steamcommunity.com/profiles/76561197991886348/", "76561197991886348", null)]
    [InlineData("https://steamcommunity.com/id/SomeUser", null, "SomeUser")]
    [InlineData("steamcommunity.com/id/SomeUser/games/?tab=all", null, "SomeUser")]
    [InlineData("SomeUser", null, "SomeUser")]
    [InlineData("", null, null)]
    [InlineData("a b c", null, null)]
    public void ParseProfileInput_AcceptsWhatUsersPaste(string input, string? steamId, string? vanity) =>
        Assert.Equal((steamId, vanity), SteamLibraryClient.ParseProfileInput(input));

    // ---- stored key ----

    [Fact]
    public void SecretProtector_RoundTripsAndDoesNotStorePlainText()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // DPAPI is Windows-only
        }

        var stored = SecretProtector.Protect("ABCDEF0123456789");

        Assert.NotNull(stored);
        Assert.DoesNotContain("ABCDEF", stored);
        Assert.Equal("ABCDEF0123456789", SecretProtector.Unprotect(stored));
        Assert.Null(SecretProtector.Protect("  "));
        Assert.Null(SecretProtector.Unprotect("not base64!"));
    }
}

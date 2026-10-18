using System.Net;
using Lancir.Core.Steam;

namespace Lancir.Tests.Services;

public class SteamAchievementsTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), "LancirAch_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
    }

    private const string PlayerJson = """
        {"playerstats":{"steamID":"1","gameName":"G","success":true,"achievements":[
          {"apiname":"A","achieved":0,"unlocktime":0,"name":"Alpha","description":"Do A"},
          {"apiname":"B","achieved":1,"unlocktime":1700000000,"name":"Bravo","description":"Do B"},
          {"apiname":"C","achieved":1,"unlocktime":1710000000,"name":"","description":""},
          {"apiname":"D","achieved":0,"unlocktime":0,"name":"Delta","description":""}
        ]}}
        """;

    private const string SchemaJson = """
        {"game":{"gameName":"G","availableGameStats":{"achievements":[
          {"name":"A","displayName":"Alpha","hidden":0,"description":"Do A","icon":"http://i/a.jpg","icongray":"http://i/a_gray.jpg"},
          {"name":"C","displayName":"Charlie","hidden":0,"description":"Do C","icon":"http://i/c.jpg","icongray":"http://i/c_gray.jpg"},
          {"name":"D","displayName":"Delta","hidden":1,"description":"","icon":"http://i/d.jpg","icongray":"http://i/d_gray.jpg"}
        ]}}}
        """;

    private const string PercentJson = """
        {"achievementpercentages":{"achievements":[
          {"name":"A","percent":50.5},{"name":"B","percent":"3.2"},{"name":"C","percent":12},{"name":"D","percent":"0.4"}
        ]}}
        """;

    [Fact]
    public void ParsePlayerAchievements_ReadsStateAndUnlockTime()
    {
        var list = SteamAchievementsClient.ParsePlayerAchievements(PlayerJson);

        Assert.Equal(4, list.Count);
        Assert.False(list[0].Achieved);
        Assert.Null(list[0].UnlockedUtc);
        Assert.True(list[1].Achieved);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime, list[1].UnlockedUtc);
    }

    [Fact]
    public void ParsePlayerAchievements_NoStatsAnswerIsEmpty() =>
        Assert.Empty(SteamAchievementsClient.ParsePlayerAchievements("""{"playerstats":{"error":"Requested app has no stats","success":false}}"""));

    [Fact]
    public void ParseGlobalPercentages_AcceptsNumbersAndText()
    {
        var map = SteamAchievementsClient.ParseGlobalPercentages(PercentJson);

        Assert.Equal(50.5, map["A"]);
        Assert.Equal(3.2, map["B"]);
        Assert.Equal(12, map["C"]);
        Assert.Equal(0.4, map["D"]);
    }

    [Fact]
    public void ParseSchema_ReadsIconsAndHidden()
    {
        var schema = SteamAchievementsClient.ParseSchema(SchemaJson);

        Assert.Equal("http://i/a_gray.jpg", schema["A"].IconGray);
        Assert.False(schema["A"].Hidden);
        Assert.True(schema["D"].Hidden);
        Assert.Empty(SteamAchievementsClient.ParseSchema("""{"game":{}}"""));
    }

    [Fact]
    public void Merge_OrdersUnlockedNewestFirstThenLockedMostCommonFirst_AndFillsGaps()
    {
        var merged = SteamAchievementsClient.Merge(
            SteamAchievementsClient.ParsePlayerAchievements(PlayerJson),
            SteamAchievementsClient.ParseSchema(SchemaJson),
            SteamAchievementsClient.ParseGlobalPercentages(PercentJson));

        Assert.Equal(["C", "B", "A", "D"], merged.Select(a => a.ApiName).ToArray());

        // C had no texts in the player answer: the schema supplies them.
        Assert.Equal("Charlie", merged[0].Name);
        Assert.Equal("Do C", merged[0].Description);
        Assert.Equal("http://i/c.jpg", merged[0].IconUrl);

        // B is missing from the schema: its own texts stay, no icon.
        Assert.Equal("Bravo", merged[1].Name);
        Assert.Null(merged[1].IconUrl);
        Assert.Equal(3.2, merged[1].GlobalPercent);

        Assert.True(merged[3].Hidden);
        Assert.False(merged[3].Achieved);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{}", null)]
    [InlineData(HttpStatusCode.BadRequest, """{"playerstats":{"error":"Requested app has no stats","success":false}}""", AchievementsStatus.NoAchievements)]
    [InlineData(HttpStatusCode.Forbidden, """{"playerstats":{"error":"Profile is not public","success":false}}""", AchievementsStatus.Private)]
    [InlineData(HttpStatusCode.Forbidden, "<html>Forbidden</html>", AchievementsStatus.InvalidKey)]
    [InlineData(HttpStatusCode.TooManyRequests, "", AchievementsStatus.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, "", AchievementsStatus.Failed)]
    public void ClassifyPlayerResponse_MapsSteamsAnswers(HttpStatusCode code, string body, AchievementsStatus? expected) =>
        Assert.Equal(expected, SteamAchievementsClient.ClassifyPlayerResponse(code, body));

    [Fact]
    public void Cache_RoundTripsAndOnlyStoresSuccesses()
    {
        var items = SteamAchievementsClient.Merge(
            SteamAchievementsClient.ParsePlayerAchievements(PlayerJson),
            SteamAchievementsClient.ParseSchema(SchemaJson),
            SteamAchievementsClient.ParseGlobalPercentages(PercentJson));
        var fetched = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        Assert.Null(AchievementCache.Load(10, _tempFolder));

        AchievementCache.Save(10, new AchievementsView(AchievementsStatus.Ok, items, fetched, false), _tempFolder);
        var loaded = AchievementCache.Load(10, _tempFolder)!;

        Assert.Equal(AchievementsStatus.Ok, loaded.Status);
        Assert.True(loaded.FromCache);
        Assert.Equal(fetched, loaded.FetchedUtc);
        Assert.Equal(items.Select(a => a.ApiName), loaded.Items.Select(a => a.ApiName));
        Assert.Equal(items[0].UnlockedUtc, loaded.Items[0].UnlockedUtc);

        AchievementCache.Save(11, new AchievementsView(AchievementsStatus.NoAchievements, [], fetched, false), _tempFolder);
        Assert.Equal(AchievementsStatus.NoAchievements, AchievementCache.Load(11, _tempFolder)!.Status);

        AchievementCache.Save(12, AchievementsView.Empty(AchievementsStatus.Failed), _tempFolder);
        Assert.Null(AchievementCache.Load(12, _tempFolder));
    }

    [Fact]
    public void Cache_BrokenFileIsAMiss()
    {
        Directory.CreateDirectory(_tempFolder);
        File.WriteAllText(Path.Combine(_tempFolder, "20.json"), "{ not json");

        Assert.Null(AchievementCache.Load(20, _tempFolder));
    }
}

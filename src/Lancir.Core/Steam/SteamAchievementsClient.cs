using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Lancir.Core.Steam;

/// <summary>One achievement of a game: its texts and icons, whether the player has it, and how rare it is.</summary>
public sealed record SteamAchievement(
    string ApiName,
    string Name,
    string Description,
    string? IconUrl,
    string? IconGrayUrl,
    bool Hidden,
    bool Achieved,
    DateTime? UnlockedUtc,
    double? GlobalPercent);

public enum AchievementsStatus
{
    Ok,

    /// <summary>The game has no achievements (Steam answers "no stats").</summary>
    NoAchievements,

    /// <summary>The profile or its "Game details" are not public.</summary>
    Private,

    InvalidKey,

    ProfileNotFound,

    /// <summary>No Steam profile / API key is set in Settings.</summary>
    NotConfigured,

    /// <summary>The game has no Steam app id.</summary>
    NoSteamId,

    /// <summary>Network or unexpected error.</summary>
    Failed,
}

/// <summary>What the details card shows: the achievements (maybe from the local cache) and why there are none.</summary>
public sealed record AchievementsView(
    AchievementsStatus Status,
    IReadOnlyList<SteamAchievement> Items,
    DateTime? FetchedUtc,
    bool FromCache)
{
    public static AchievementsView Empty(AchievementsStatus status) => new(status, [], null, false);
}

public interface ISteamAchievementsClient
{
    /// <summary>Fetches a player's achievements for one game, with texts, icons and global rarity.</summary>
    Task<AchievementsView> GetAchievementsAsync(
        string apiKey, string steamId, int appId, string language, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads achievements through the official Web API: GetPlayerAchievements (what the player has),
/// GetSchemaForGame (texts, icons, hidden flag) and GetGlobalAchievementPercentagesForApp (rarity, no key).
/// The key is only sent to api.steampowered.com and never logged.
/// </summary>
public sealed class SteamAchievementsClient : ISteamAchievementsClient
{
    private const string ApiBase = "https://api.steampowered.com/";

    private readonly HttpClient _httpClient;

    public SteamAchievementsClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Lancir/1.0");
        }
    }

    public async Task<AchievementsView> GetAchievementsAsync(
        string apiKey, string steamId, int appId, string language, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = Uri.EscapeDataString(apiKey);
            var lang = Uri.EscapeDataString(language);

            using var playerResponse = await _httpClient.GetAsync(
                $"{ApiBase}ISteamUserStats/GetPlayerAchievements/v1/?key={key}&steamid={Uri.EscapeDataString(steamId)}&appid={appId}&l={lang}",
                cancellationToken);
            var playerBody = await playerResponse.Content.ReadAsStringAsync(cancellationToken);

            var failure = ClassifyPlayerResponse(playerResponse.StatusCode, playerBody);
            if (failure is { } status)
            {
                return AchievementsView.Empty(status);
            }

            var player = ParsePlayerAchievements(playerBody);
            if (player.Count == 0)
            {
                return AchievementsView.Empty(AchievementsStatus.NoAchievements);
            }

            // Texts/icons and rarity are decoration: if either request fails the achievements are still shown.
            var schemaTask = FetchOrNullAsync(
                $"{ApiBase}ISteamUserStats/GetSchemaForGame/v2/?key={key}&appid={appId}&l={lang}", cancellationToken);
            var percentTask = FetchOrNullAsync(
                $"{ApiBase}ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId}&format=json", cancellationToken);
            await Task.WhenAll(schemaTask, percentTask);

            var schema = await schemaTask is { } schemaJson
                ? ParseSchema(schemaJson)
                : new Dictionary<string, SchemaAchievement>();
            var percentages = await percentTask is { } percentJson
                ? ParseGlobalPercentages(percentJson)
                : new Dictionary<string, double>();

            return new AchievementsView(AchievementsStatus.Ok, Merge(player, schema, percentages), DateTime.UtcNow, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return AchievementsView.Empty(AchievementsStatus.Failed);
        }
    }

    private async Task<string?> FetchOrNullAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.GetStringAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a GetPlayerAchievements answer to a failure, or null when it carries achievements. Steam uses 403 both for a
    /// private profile (body says so) and for a bad key, and 400 / "no stats" for a game without achievements.
    /// </summary>
    public static AchievementsStatus? ClassifyPlayerResponse(HttpStatusCode code, string body)
    {
        var lower = body.ToLowerInvariant();
        if (lower.Contains("not public"))
        {
            return AchievementsStatus.Private;
        }

        if (lower.Contains("no stats"))
        {
            return AchievementsStatus.NoAchievements;
        }

        return code switch
        {
            HttpStatusCode.OK => null,
            HttpStatusCode.BadRequest => AchievementsStatus.NoAchievements,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AchievementsStatus.InvalidKey,
            _ => AchievementsStatus.Failed,
        };
    }

    public sealed record PlayerAchievement(string ApiName, bool Achieved, DateTime? UnlockedUtc, string Name, string Description);

    public sealed record SchemaAchievement(string DisplayName, string Description, string? Icon, string? IconGray, bool Hidden);

    public static IReadOnlyList<PlayerAchievement> ParsePlayerAchievements(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new List<PlayerAchievement>();
        if (!document.RootElement.TryGetProperty("playerstats", out var stats) ||
            !stats.TryGetProperty("achievements", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in list.EnumerateArray())
        {
            var apiName = GetString(item, "apiname");
            if (string.IsNullOrEmpty(apiName))
            {
                continue;
            }

            var achieved = item.TryGetProperty("achieved", out var a) && a.TryGetInt32(out var flag) && flag != 0;
            DateTime? unlocked = achieved && item.TryGetProperty("unlocktime", out var u) && u.TryGetInt64(out var t) && t > 0
                ? DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime
                : null;
            result.Add(new PlayerAchievement(apiName, achieved, unlocked, GetString(item, "name") ?? "", GetString(item, "description") ?? ""));
        }

        return result;
    }

    public static IReadOnlyDictionary<string, SchemaAchievement> ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, SchemaAchievement>();
        if (!document.RootElement.TryGetProperty("game", out var game) ||
            !game.TryGetProperty("availableGameStats", out var stats) ||
            !stats.TryGetProperty("achievements", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in list.EnumerateArray())
        {
            var name = GetString(item, "name");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var hidden = item.TryGetProperty("hidden", out var h) && h.TryGetInt32(out var flag) && flag != 0;
            result[name] = new SchemaAchievement(
                GetString(item, "displayName") ?? "", GetString(item, "description") ?? "",
                GetString(item, "icon"), GetString(item, "icongray"), hidden);
        }

        return result;
    }

    public static IReadOnlyDictionary<string, double> ParseGlobalPercentages(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, double>();
        if (!document.RootElement.TryGetProperty("achievementpercentages", out var root) ||
            !root.TryGetProperty("achievements", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in list.EnumerateArray())
        {
            var name = GetString(item, "name");
            if (string.IsNullOrEmpty(name) || !item.TryGetProperty("percent", out var p))
            {
                continue;
            }

            // Steam sends the percentage as a number in some versions and as text in others.
            var ok = p.ValueKind == JsonValueKind.Number
                ? p.TryGetDouble(out var number)
                : double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            if (ok)
            {
                result[name] = number;
            }
        }

        return result;
    }

    /// <summary>
    /// Joins the three answers. The player's texts win when present (they are in the requested language), the schema fills
    /// in icons and hidden flags. Unlocked ones come first, newest first; then the rest, most common first.
    /// </summary>
    public static IReadOnlyList<SteamAchievement> Merge(
        IReadOnlyList<PlayerAchievement> player,
        IReadOnlyDictionary<string, SchemaAchievement> schema,
        IReadOnlyDictionary<string, double> percentages)
    {
        var items = player.Select(p =>
        {
            schema.TryGetValue(p.ApiName, out var s);
            var name = !string.IsNullOrWhiteSpace(p.Name) ? p.Name : s?.DisplayName;
            var description = !string.IsNullOrWhiteSpace(p.Description) ? p.Description : s?.Description;
            return new SteamAchievement(
                p.ApiName,
                string.IsNullOrWhiteSpace(name) ? p.ApiName : name,
                description ?? "",
                s?.Icon,
                s?.IconGray,
                s?.Hidden ?? false,
                p.Achieved,
                p.UnlockedUtc,
                percentages.TryGetValue(p.ApiName, out var percent) ? percent : null);
        });

        return items
            .OrderByDescending(a => a.Achieved)
            .ThenByDescending(a => a.UnlockedUtc ?? DateTime.MinValue)
            .ThenByDescending(a => a.GlobalPercent ?? -1)
            .ToList();
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lancir.Core.Steam;

/// <summary>One game of a Steam account's library, as listed by the Steam Web API.</summary>
public sealed record SteamOwnedGame(int AppId, string Name, long PlaytimeSeconds, DateTime? LastPlayedUtc);

public enum SteamFetchStatus
{
    Ok,

    /// <summary>The profile or its "Game details" are not public: Steam answered with no game list.</summary>
    Private,

    /// <summary>The API key was rejected.</summary>
    InvalidKey,

    /// <summary>The profile address could not be turned into a SteamID64.</summary>
    ProfileNotFound,

    /// <summary>Network or unexpected error.</summary>
    Failed,
}

public sealed record SteamFetchResult(SteamFetchStatus Status, IReadOnlyList<SteamOwnedGame> Games)
{
    public static SteamFetchResult Fail(SteamFetchStatus status) => new(status, []);
}

public interface ISteamLibraryClient
{
    /// <summary>Resolves a profile address / custom-URL name / SteamID64 to a SteamID64; null if it can't be found.</summary>
    Task<string?> ResolveSteamIdAsync(string profile, CancellationToken cancellationToken = default);

    /// <summary>Lists the games a Steam account owns (including free-to-play games it has played) with their playtime.</summary>
    Task<SteamFetchResult> GetOwnedGamesAsync(string apiKey, string steamId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads a user's Steam library through the official Web API (IPlayerService/GetOwnedGames), which needs the user's
/// own free API key and a profile whose "Game details" are public. The key is only ever sent to api.steampowered.com
/// and is never logged.
/// </summary>
public sealed partial class SteamLibraryClient : ISteamLibraryClient
{
    private readonly HttpClient _httpClient;

    public SteamLibraryClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Lancir/1.0");
        }
    }

    public async Task<string?> ResolveSteamIdAsync(string profile, CancellationToken cancellationToken = default)
    {
        var (steamId, vanity) = ParseProfileInput(profile);
        if (steamId is not null)
        {
            return steamId;
        }

        if (vanity is null)
        {
            return null;
        }

        try
        {
            // The public XML view of a profile works without a key and names the SteamID64.
            var xml = await _httpClient.GetStringAsync(
                $"https://steamcommunity.com/id/{Uri.EscapeDataString(vanity)}/?xml=1", cancellationToken);
            var match = SteamId64Regex().Match(xml);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<SteamFetchResult> GetOwnedGamesAsync(string apiKey, string steamId, CancellationToken cancellationToken = default)
    {
        var url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/" +
                  $"?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId)}" +
                  "&include_appinfo=1&include_played_free_games=1&format=json";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return SteamFetchResult.Fail(SteamFetchStatus.InvalidKey);
            }

            if (!response.IsSuccessStatusCode)
            {
                return SteamFetchResult.Fail(SteamFetchStatus.Failed);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var games = ParseOwnedGames(json);
            return games is null
                ? SteamFetchResult.Fail(SteamFetchStatus.Private)
                : new SteamFetchResult(SteamFetchStatus.Ok, games);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return SteamFetchResult.Fail(SteamFetchStatus.Failed);
        }
    }

    /// <summary>
    /// Reads a GetOwnedGames answer. Returns null when it carries no game list at all, which is how Steam answers for a
    /// private profile or private game details (an account that owns nothing still has "game_count": 0).
    /// </summary>
    public static IReadOnlyList<SteamOwnedGame>? ParseOwnedGames(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("response", out var response) ||
            response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("game_count", out _))
        {
            return null;
        }

        var result = new List<SteamOwnedGame>();
        if (!response.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var game in games.EnumerateArray())
        {
            if (!game.TryGetProperty("appid", out var id) || !id.TryGetInt32(out var appId))
            {
                continue;
            }

            var name = game.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()
                : null;
            var minutes = game.TryGetProperty("playtime_forever", out var p) && p.TryGetInt64(out var m) ? m : 0;
            DateTime? lastPlayed = game.TryGetProperty("rtime_last_played", out var r) && r.TryGetInt64(out var t) && t > 0
                ? DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime
                : null;

            result.Add(new SteamOwnedGame(appId, string.IsNullOrWhiteSpace(name) ? $"App {appId}" : name!, minutes * 60, lastPlayed));
        }

        return result;
    }

    /// <summary>
    /// Understands what a user may paste: a 17-digit SteamID64, steamcommunity.com/profiles/&lt;id&gt;,
    /// steamcommunity.com/id/&lt;name&gt; or just the custom-URL name. Exactly one of the two results is set (or none).
    /// </summary>
    public static (string? SteamId64, string? Vanity) ParseProfileInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (null, null);
        }

        var text = input.Trim();
        if (SteamIdOnlyRegex().IsMatch(text))
        {
            return (text, null);
        }

        var profiles = ProfilesUrlRegex().Match(text);
        if (profiles.Success)
        {
            return (profiles.Groups[1].Value, null);
        }

        var vanityUrl = VanityUrlRegex().Match(text);
        if (vanityUrl.Success)
        {
            return (null, vanityUrl.Groups[1].Value);
        }

        return VanityOnlyRegex().IsMatch(text) ? (null, text) : (null, null);
    }

    [GeneratedRegex(@"<steamID64>(\d{17})</steamID64>")]
    private static partial Regex SteamId64Regex();

    [GeneratedRegex(@"^\d{17}$")]
    private static partial Regex SteamIdOnlyRegex();

    [GeneratedRegex(@"steamcommunity\.com/profiles/(\d{17})", RegexOptions.IgnoreCase)]
    private static partial Regex ProfilesUrlRegex();

    [GeneratedRegex(@"steamcommunity\.com/id/([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex VanityUrlRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{2,32}$")]
    private static partial Regex VanityOnlyRegex();
}

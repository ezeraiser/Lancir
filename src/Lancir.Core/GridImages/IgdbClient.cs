using System.Net.Http.Headers;
using System.Text.Json;
using Lancir.Core.Models;

namespace Lancir.Core.GridImages;

public sealed class IgdbClient : IIgdbClient
{
    private readonly HttpClient _httpClient;
    private string? _cachedAccessToken;
    private DateTimeOffset _cachedTokenExpiresAtUtc = DateTimeOffset.MinValue;

    public IgdbClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IgdbCoverResult?> FindCoverAsync(
        string gameName, string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        var accessToken = await GetAccessTokenAsync(clientId, clientSecret, cancellationToken);
        if (accessToken is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent($"search \"{EscapeQueryValue(gameName)}\"; fields cover.url; limit 1;");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var firstResult = document.RootElement[0];
        if (!firstResult.TryGetProperty("cover", out var cover) ||
            !cover.TryGetProperty("url", out var urlProp) ||
            urlProp.GetString() is not { Length: > 0 } rawUrl)
        {
            return null;
        }

        // IGDB returns protocol-relative thumbnail URLs; upgrade to a larger, HTTPS image.
        var imageUrl = "https:" + rawUrl.Replace("t_thumb", "t_cover_big");
        return new IgdbCoverResult { ImageUrl = imageUrl };
    }

    public async Task<IgdbGameMatch?> FindGameAsync(
        string gameName, string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        var results = await SearchGamesAsync(gameName, clientId, clientSecret, limit: 1, cancellationToken);
        return results.FirstOrDefault();
    }

    public async Task<IReadOnlyList<IgdbGameMatch>> SearchGamesAsync(
        string gameName, string clientId, string clientSecret, int limit = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return [];
        }

        var accessToken = await GetAccessTokenAsync(clientId, clientSecret, cancellationToken);
        if (accessToken is null)
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            $"search \"{EscapeQueryValue(gameName)}\"; " +
            "fields id,name,summary,cover.url,artworks.url,genres.name,total_rating,first_release_date; " +
            $"limit {limit};");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<IgdbGameMatch>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idProp) || !item.TryGetProperty("name", out var nameProp))
            {
                continue;
            }

            string? coverImageUrl = null;
            if (item.TryGetProperty("cover", out var cover) &&
                cover.TryGetProperty("url", out var urlProp) &&
                urlProp.GetString() is { Length: > 0 } rawUrl)
            {
                coverImageUrl = "https:" + rawUrl.Replace("t_thumb", "t_cover_big");
            }

            string? backgroundImageUrl = null;
            if (item.TryGetProperty("artworks", out var artworks) &&
                artworks.ValueKind == JsonValueKind.Array && artworks.GetArrayLength() > 0 &&
                artworks[0].TryGetProperty("url", out var artworkUrlProp) &&
                artworkUrlProp.GetString() is { Length: > 0 } rawArtworkUrl)
            {
                backgroundImageUrl = "https:" + rawArtworkUrl.Replace("t_thumb", "t_1080p");
            }

            string? genres = null;
            if (item.TryGetProperty("genres", out var genresProp) && genresProp.ValueKind == JsonValueKind.Array)
            {
                var genreNames = genresProp.EnumerateArray()
                    .Select(g => g.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                if (genreNames.Count > 0)
                {
                    genres = string.Join(", ", genreNames);
                }
            }

            double? communityRating = item.TryGetProperty("total_rating", out var ratingProp) && ratingProp.ValueKind == JsonValueKind.Number
                ? ratingProp.GetDouble()
                : null;

            DateTime? releaseDateUtc = item.TryGetProperty("first_release_date", out var releaseProp) && releaseProp.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(releaseProp.GetInt64()).UtcDateTime
                : null;

            results.Add(new IgdbGameMatch
            {
                IgdbId = idProp.GetInt32(),
                Name = nameProp.GetString() ?? gameName,
                Summary = item.TryGetProperty("summary", out var summaryProp) ? summaryProp.GetString() : null,
                CoverImageUrl = coverImageUrl,
                BackgroundImageUrl = backgroundImageUrl,
                Genres = genres,
                CommunityRating = communityRating,
                ReleaseDateUtc = releaseDateUtc,
            });
        }

        return results;
    }

    public async Task<IgdbUpdateCheckResult?> CheckForUpdateAsync(
        string gameName,
        string clientId,
        string clientSecret,
        DateTime? previousUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        var accessToken = await GetAccessTokenAsync(clientId, clientSecret, cancellationToken);
        if (accessToken is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            $"search \"{EscapeQueryValue(gameName)}\"; fields id,name,updated_at; limit 1;");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var firstResult = document.RootElement[0];
        if (!firstResult.TryGetProperty("id", out var idProp) ||
            !firstResult.TryGetProperty("updated_at", out var updatedAtProp) ||
            !firstResult.TryGetProperty("name", out var nameProp))
        {
            return null;
        }

        // IGDB returns updated_at as a Unix timestamp (seconds).
        var updatedAtUtc = DateTimeOffset.FromUnixTimeSeconds(updatedAtProp.GetInt64()).UtcDateTime;
        var hasChanged = previousUpdatedAtUtc is not null && updatedAtUtc > previousUpdatedAtUtc;

        return new IgdbUpdateCheckResult
        {
            IgdbId = idProp.GetInt32(),
            Name = nameProp.GetString() ?? gameName,
            UpdatedAtUtc = updatedAtUtc,
            HasChanged = hasChanged,
        };
    }

    private async Task<string?> GetAccessTokenAsync(
        string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _cachedTokenExpiresAtUtc)
        {
            return _cachedAccessToken;
        }

        var query = $"client_id={Uri.EscapeDataString(clientId)}" +
                    $"&client_secret={Uri.EscapeDataString(clientSecret)}" +
                    "&grant_type=client_credentials";

        using var response = await _httpClient.PostAsync(
            $"https://id.twitch.tv/oauth2/token?{query}", content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("access_token", out var tokenProp))
        {
            return null;
        }

        var expiresInSeconds = document.RootElement.TryGetProperty("expires_in", out var expiresProp)
            ? expiresProp.GetInt64()
            : 0;

        _cachedAccessToken = tokenProp.GetString();
        // Refresh a bit early to avoid racing against actual expiry.
        _cachedTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresInSeconds - 60));

        return _cachedAccessToken;
    }

    private static string EscapeQueryValue(string value) => value.Replace("\"", "\\\"");
}

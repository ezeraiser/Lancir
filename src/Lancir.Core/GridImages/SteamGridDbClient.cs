using System.Net.Http.Headers;
using System.Text.Json;
using Lancir.Core.Models;

namespace Lancir.Core.GridImages;

public sealed class SteamGridDbClient : ISteamGridDbClient
{
    private readonly HttpClient _httpClient;

    public SteamGridDbClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<SteamGridDbGameMatch>> SearchGamesAsync(
        string gameName, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return [];
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(gameName)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean() ||
            !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<SteamGridDbGameMatch>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idProp) || !item.TryGetProperty("name", out var nameProp))
            {
                continue;
            }

            var gridDbGameId = idProp.GetInt32();
            var coverUrl = (await GetGridImagesAsync(gridDbGameId, apiKey, cancellationToken)).FirstOrDefault()?.Url;
            var backgroundUrl = (await GetHeroImagesAsync(gridDbGameId, apiKey, cancellationToken)).FirstOrDefault()?.Url;

            DateTime? releaseDateUtc = item.TryGetProperty("release_date", out var releaseProp) && releaseProp.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(releaseProp.GetInt64()).UtcDateTime
                : null;

            results.Add(new SteamGridDbGameMatch
            {
                GridDbGameId = gridDbGameId,
                Name = nameProp.GetString() ?? gameName,
                CoverImageUrl = coverUrl,
                BackgroundImageUrl = backgroundUrl,
                ReleaseDateUtc = releaseDateUtc,
            });
        }

        return results;
    }

    public Task<IReadOnlyList<GridImage>> GetGridImagesAsync(
        int gridDbGameId, string apiKey, CancellationToken cancellationToken = default) =>
        GetImagesAsync("grids", gridDbGameId, apiKey, cancellationToken);

    public Task<IReadOnlyList<GridImage>> GetHeroImagesAsync(
        int gridDbGameId, string apiKey, CancellationToken cancellationToken = default) =>
        GetImagesAsync("heroes", gridDbGameId, apiKey, cancellationToken);

    private async Task<IReadOnlyList<GridImage>> GetImagesAsync(
        string endpoint, int gridDbGameId, string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return [];
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://www.steamgriddb.com/api/v2/{endpoint}/game/{gridDbGameId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean() ||
            !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var images = new List<GridImage>();
        foreach (var item in data.EnumerateArray())
        {
            var url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var thumb = item.TryGetProperty("thumb", out var thumbProp) ? thumbProp.GetString() : null;

            images.Add(new GridImage
            {
                Type = GridImageType.Grid,
                Url = url,
                ThumbnailUrl = thumb,
            });
        }

        return images;
    }
}

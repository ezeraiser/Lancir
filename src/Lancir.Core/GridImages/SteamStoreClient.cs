using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lancir.Core.Models;
using Lancir.Core.Services;

namespace Lancir.Core.GridImages;

public sealed partial class SteamStoreClient : ISteamStoreClient
{
    private static readonly string[] ImageBases =
    [
        "https://cdn.cloudflare.steamstatic.com/steam/apps",
        "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps",
    ];

    private static readonly string[] ModeKeywords =
        ["tek oyunculu", "çok oyunculu", "co-op", "pvp", "single-player", "multi-player"];

    private static readonly CultureInfo Turkish = new("tr-TR");
    private static readonly string[] DateFormats = ["d MMM, yyyy", "d MMMM, yyyy", "d MMM yyyy", "d MMMM yyyy", "MMM yyyy", "yyyy"];

    private readonly HttpClient _httpClient;

    public SteamStoreClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Lancir/1.0");
        }
    }

    public async Task<IReadOnlyList<SteamSearchResult>> SearchAsync(string term, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(term)}&l={AppLanguage.SteamLanguage}&cc={AppLanguage.SteamCountry}";
            using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("items", out var items))
            {
                return [];
            }

            var results = new List<SteamSearchResult>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) && type.GetString() != "app")
                {
                    continue;
                }

                if (item.TryGetProperty("id", out var id) && item.TryGetProperty("name", out var name) &&
                    name.GetString() is { Length: > 0 } nameText)
                {
                    results.Add(new SteamSearchResult(id.GetInt32(), nameText));
                }
            }

            return results;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return [];
        }
    }

    public async Task<SteamAppDetails?> GetDetailsAsync(int appId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l={AppLanguage.SteamLanguage}&cc={AppLanguage.SteamCountry}";
            using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out var entry) ||
                !entry.TryGetProperty("success", out var success) || !success.GetBoolean() ||
                !entry.TryGetProperty("data", out var data))
            {
                return null;
            }

            if (GetString(data, "type") is { } type && type != "game")
            {
                return null;
            }

            var screenshots = new List<string>();
            if (data.TryGetProperty("screenshots", out var shots))
            {
                foreach (var shot in shots.EnumerateArray())
                {
                    if (GetString(shot, "path_full") is { } path)
                    {
                        screenshots.Add(path);
                    }
                }
            }

            string? trailerUrl = null, trailerThumb = null;
            if (data.TryGetProperty("movies", out var movies) && movies.GetArrayLength() > 0)
            {
                var movie = movies[0];
                trailerThumb = GetString(movie, "thumbnail");
                // Steam serves trailers as streaming manifests (HLS), which the app's own video viewer plays; an mp4/webm link is used
                // when there is one, and the store page (where the video plays in a browser) when there is nothing else.
                trailerUrl = GetString(movie, "hls_h264") ?? GetNested(movie, "mp4", "max") ?? GetNested(movie, "mp4", "480")
                             ?? GetNested(movie, "webm", "max") ?? GetNested(movie, "webm", "480")
                             ?? $"https://store.steampowered.com/app/{appId}/";
            }

            var rich = HtmlToText(GetString(data, "about_the_game") ?? GetString(data, "detailed_description"));

            return new SteamAppDetails
            {
                AppId = appId,
                Name = GetString(data, "name") ?? "",
                ShortDescription = GetString(data, "short_description"),
                FullDescription = StripMedia(rich),
                HeaderImageUrl = GetString(data, "header_image"),
                Genres = JoinDescriptions(data, "genres"),
                ReleaseDateUtc = ParseReleaseDate(data),
                MetacriticScore = data.TryGetProperty("metacritic", out var meta) && meta.TryGetProperty("score", out var score)
                    ? score.GetInt32()
                    : null,
                Extra = new GameExtraDetails
                {
                    RichDescription = rich,
                    Developers = JoinStrings(data, "developers"),
                    Publishers = JoinStrings(data, "publishers"),
                    Modes = JoinModes(data),
                    Languages = CleanLanguages(GetString(data, "supported_languages")),
                    AgeRating = ParseAgeRating(data),
                    Website = GetString(data, "website"),
                    ScreenshotUrls = screenshots,
                    TrailerUrl = trailerUrl,
                    TrailerThumbnailUrl = trailerThumb,
                },
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<string?> ResolveCoverUrlAsync(int appId, CancellationToken cancellationToken = default) =>
        await FirstExistingAsync(await GetAssetUrlsAsync(appId, ["library_capsule_2x", "library_capsule"], cancellationToken), cancellationToken)
        ?? await FirstExistingAsync(LegacyUrls(appId, ["library_600x900_2x.jpg", "library_600x900.jpg"]), cancellationToken);

    public async Task<string?> ResolveHeroUrlAsync(int appId, CancellationToken cancellationToken = default) =>
        await FirstExistingAsync(await GetAssetUrlsAsync(appId, ["library_hero_2x", "library_hero"], cancellationToken), cancellationToken)
        ?? await FirstExistingAsync(LegacyUrls(appId, ["library_hero.jpg"]), cancellationToken);

    /// <summary>
    /// Newer Steam apps keep their artwork in hash-named folders; the store browse API lists the exact
    /// file names. Returns the URLs for the requested asset names, in the order given.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetAssetUrlsAsync(int appId, string[] assetNames, CancellationToken cancellationToken)
    {
        try
        {
            var input = $"{{\"ids\":[{{\"appid\":{appId}}}],\"context\":{{\"language\":\"english\",\"country_code\":\"US\",\"steam_realm\":1}},\"data_request\":{{\"include_assets\":true}}}}";
            var url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=" + Uri.EscapeDataString(input);
            using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var assets = document.RootElement.GetProperty("response").GetProperty("store_items")[0].GetProperty("assets");
            var format = GetString(assets, "asset_url_format");
            if (format is null)
            {
                return [];
            }

            return assetNames
                .Select(name => GetString(assets, name))
                .Where(file => file is not null)
                .Select(file => "https://shared.cloudflare.steamstatic.com/store_item_assets/" + format.Replace("${FILENAME}", file))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return [];
        }
    }

    private static IEnumerable<string> LegacyUrls(int appId, string[] fileNames) =>
        fileNames.SelectMany(file => ImageBases.Select(baseUrl => $"{baseUrl}/{appId}/{file}"));

    private async Task<string?> FirstExistingAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        foreach (var url in urls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return url;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // try the next candidate
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNested(JsonElement element, string parent, string child) =>
        element.TryGetProperty(parent, out var inner) ? GetString(inner, child) : null;

    private static string? JoinStrings(JsonElement data, string property)
    {
        if (!data.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = array.EnumerateArray().Select(e => e.GetString()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return values.Count == 0 ? null : string.Join(", ", values);
    }

    private static string? JoinDescriptions(JsonElement data, string property)
    {
        if (!data.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = array.EnumerateArray().Select(e => GetString(e, "description")).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return values.Count == 0 ? null : string.Join(", ", values);
    }

    private static string? JoinModes(JsonElement data)
    {
        if (!data.TryGetProperty("categories", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var modes = array.EnumerateArray()
            .Select(e => GetString(e, "description"))
            .Where(d => d is not null && ModeKeywords.Any(k => d.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return modes.Count == 0 ? null : string.Join(", ", modes);
    }

    private static string? ParseAgeRating(JsonElement data)
    {
        if (!data.TryGetProperty("ratings", out var ratings) || ratings.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (ratings.TryGetProperty("pegi", out var pegi) && GetString(pegi, "rating") is { Length: > 0 } pegiRating)
        {
            return $"PEGI {pegiRating}";
        }

        if (ratings.TryGetProperty("esrb", out var esrb) && GetString(esrb, "rating") is { Length: > 0 } esrbRating)
        {
            return $"ESRB {esrbRating.ToUpperInvariant()}";
        }

        if (ratings.TryGetProperty("usk", out var usk) && GetString(usk, "rating") is { Length: > 0 } uskRating)
        {
            return $"USK {uskRating}";
        }

        return null;
    }

    private static DateTime? ParseReleaseDate(JsonElement data)
    {
        if (!data.TryGetProperty("release_date", out var release) ||
            (release.TryGetProperty("coming_soon", out var soon) && soon.ValueKind == JsonValueKind.True) ||
            GetString(release, "date") is not { Length: > 0 } text)
        {
            return null;
        }

        foreach (var culture in new[] { Turkish, CultureInfo.InvariantCulture })
        {
            if (DateTime.TryParseExact(text.Replace(".", ""), DateFormats, culture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>Steam sends the language list as HTML with "*" marking full audio support and a footnote.</summary>
    private static string? CleanLanguages(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var listPart = html.Split("<br>", StringSplitOptions.RemoveEmptyEntries)[0];
        var text = HtmlTagRegex().Replace(listPart, "").Replace("*", "");
        var languages = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return languages.Length == 0 ? null : string.Join(", ", languages);
    }

    /// <summary>
    /// Converts Steam's HTML store text into simple plain text: media is dropped, headings become
    /// "# heading", list items "- item", and paragraphs are separated by blank lines.
    /// </summary>
    private static string? HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = VideoRegex().Replace(html, VideoMarker);
        text = ImageTagRegex().Replace(text, ImageMarker);
        text = MediaBlockRegex().Replace(text, "");
        text = HeadingRegex().Replace(text, HeadingText);
        text = ListItemRegex().Replace(text, "\n- ");
        text = LineBreakRegex().Replace(text, "\n");
        text = ParagraphStartRegex().Replace(text, "\n");
        text = WebUtility.HtmlDecode(HtmlTagRegex().Replace(text, ""));

        var lines = text.Split('\n').Select(line => line.Trim());
        text = BlankRunRegex().Replace(string.Join("\n", lines), "\n\n").Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("<(script|style)[^>]*>.*?</\\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex MediaBlockRegex();

    [GeneratedRegex("<video[^>]*>.*?</video>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex VideoRegex();

    [GeneratedRegex("<video[^>]*?width=\"?(\\d+)\"?[^>]*?height=\"?(\\d+)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex VideoSizeRegex();

    [GeneratedRegex("<source[^>]*?src=\"([^\"]+\\.(?:mp4|webm)[^\"]*)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex VideoSourceRegex();

    [GeneratedRegex("data-big-src=\"?([^\\s\">]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BigSrcRegex();

    [GeneratedRegex("\\ssrc=\"?([^\\s\">]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SrcRegex();

    [GeneratedRegex("^(?:# )?!(video|image) ", RegexOptions.Multiline)]
    private static partial Regex MediaLineRegex();

    /// <summary>
    /// "!video url|width|height" on its own line, or nothing when the clip has no usable source. The mp4
    /// source is preferred because Windows (Explorer thumbnails, the default player) understands it best.
    /// </summary>
    private static string VideoMarker(Match match)
    {
        var source = VideoSourceRegex().Matches(match.Value)
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .OrderBy(url => url.Contains(".mp4", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
        if (source is null)
        {
            return "";
        }

        var size = VideoSizeRegex().Match(match.Value);
        var width = size.Success ? size.Groups[1].Value : "600";
        var height = size.Success ? size.Groups[2].Value : "338";
        return $"\n!video {source}|{width}|{height}\n";
    }

    /// <summary>
    /// "# heading" for the text of a heading; a picture or clip the page put inside the heading keeps its own
    /// marker line (otherwise "# !image url" would show up as heading text).
    /// </summary>
    private static string HeadingText(Match match)
    {
        var lines = HtmlTagRegex().Replace(match.Groups[1].Value, "")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => line.StartsWith("!image ", StringComparison.Ordinal) || line.StartsWith("!video ", StringComparison.Ordinal)
                ? line
                : "# " + line);
        return "\n\n" + string.Join("\n", lines) + "\n\n";
    }

    /// <summary>"!image url" on its own line; the large version is preferred when the page offers one.</summary>
    private static string ImageMarker(Match match)
    {
        var big = BigSrcRegex().Match(match.Value);
        var src = big.Success ? big : SrcRegex().Match(match.Value);
        return src.Success ? $"\n!image {WebUtility.HtmlDecode(src.Groups[1].Value)}\n" : "";
    }

    /// <summary>The plain description: the rich text without its media lines.</summary>
    private static string? StripMedia(string? rich)
    {
        if (rich is null)
        {
            return null;
        }

        var plain = MediaLineRegex().Replace(rich, "\u0001");
        var lines = plain.Split('\n').Where(line => !line.StartsWith('\u0001'));
        var text = BlankRunRegex().Replace(string.Join("\n", lines), "\n\n").Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex("<img[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageTagRegex();

    [GeneratedRegex("<h[1-6][^>]*>(.*?)</h[1-6]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex("<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex("<br\\s*/?>|</p>|</li>|</ul>|</ol>|</div>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex("<p[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphStartRegex();

    [GeneratedRegex("\n{3,}")]
    private static partial Regex BlankRunRegex();
}

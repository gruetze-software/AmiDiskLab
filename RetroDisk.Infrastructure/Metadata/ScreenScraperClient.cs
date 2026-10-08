using AmiDiskLab.Core.Models;
using AmiDiskLab.Infrastructure.FileSystem;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace AmiDiskLab.Infrastructure.Metadata;

public sealed record ScreenScraperAccess(string UserId, string UserPassword);
public sealed record ScreenScraperUserInfo(string? UserId, int MaxThreads, int RequestsToday,
    int FailedRequestsToday, int MaxRequestsPerMinute, int MaxRequestsPerDay,
    int MaxFailedRequestsPerDay)
{
    public int RequestsRemaining => Math.Max(0, MaxRequestsPerDay - RequestsToday);
    public int FailedRequestsRemaining => Math.Max(0, MaxFailedRequestsPerDay - FailedRequestsToday);
}

public sealed class ScreenScraperClient(HttpClient httpClient)
{
    public const string ProxyRoot = "https://amidisklab-api.c-schaef.workers.dev";
    private const string Endpoint = "jeuInfos.php";
    private const string SearchEndpoint = "jeuRecherche.php";
    private readonly object _rateLock = new();
    private readonly Queue<DateTimeOffset> _requestTimes = new();
    private SemaphoreSlim _requestSlots = new(1, 1);
    private int _maxRequestsPerMinute = 20;
    public event Action<ScreenScraperUserInfo>? UserInfoUpdated;

    public void ConfigureLimits(ScreenScraperUserInfo info)
    {
        _requestSlots = new SemaphoreSlim(Math.Max(1, info.MaxThreads), Math.Max(1, info.MaxThreads));
        _maxRequestsPerMinute = Math.Max(1, info.MaxRequestsPerMinute);
    }

    public async Task<ScreenScraperUserInfo> GetUserInfoAsync(ScreenScraperAccess access,
        CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync("ssuserInfos.php", BaseQuery(access), cancellationToken)
            ?? throw new IOException("ScreenScraper did not return account information.");
        return ParseUserInfo(document) ?? throw new IOException("ScreenScraper account information is incomplete.");
    }

    public Task<IReadOnlyList<MetadataSuggestion>> SearchAdfAsync(string adfPath,
        ScreenScraperAccess access, CancellationToken cancellationToken = default) =>
        SearchAdfAsync(adfPath, access, ScreenScraperPreferences.Default, cancellationToken);

    public async Task<IReadOnlyList<MetadataSuggestion>> SearchAdfAsync(string adfPath,
        ScreenScraperAccess access, ScreenScraperPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        preferences.Validate();
        var exact = await LookupAdfAsync(adfPath, access, preferences, cancellationToken);
        var matches = new List<MetadataSuggestion>();
        if (exact is not null) matches.Add(exact);
        var stem = Path.GetFileNameWithoutExtension(adfPath);
        var title = DiskSetGrouper.BaseTitle(adfPath) ??
            AdfMetadataSuggester.SuggestFromFileName(stem)?.Metadata.Title ??
            Regex.Replace(stem.Replace('_', ' '), @"\s+(?:19|20)\d{2}.*$", "",
                RegexOptions.CultureInvariant).Trim();
        title = Regex.Replace(title, @"\s+v\d+(?:\.\d+)*$", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        if (exact is null) title = RemoveParentheticalFileTags(title);
        foreach (var candidate in NameSearchCandidates(title))
        {
            var query = BaseQuery(access);
            query["systemeid"] = SystemId(adfPath);
            query["recherche"] = candidate;
            var document = await GetDocumentAsync(SearchEndpoint, query, cancellationToken);
            if (document is null) continue;
            var suggestions = new List<MetadataSuggestion>();
            foreach (var game in document.Descendants().Where(node => node.Name.LocalName == "jeu").Take(30))
            {
                var suggestion = ParseGame(game, null,
                    $"ScreenScraper name search for '{candidate}' (hash not verified)", preferences);
                if (suggestion is not null)
                    suggestions.Add(await ResolvePublisherLogoAsync(suggestion, game, access, cancellationToken));
            }
            foreach (var suggestion in suggestions)
                if (!matches.Any(existing => string.Equals(existing.Metadata.Title, suggestion.Metadata.Title,
                        StringComparison.OrdinalIgnoreCase)))
                    matches.Add(suggestion);
            if (suggestions.Count > 0) break;
        }
        return matches;
    }

    internal static IReadOnlyList<string> NameSearchCandidates(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return [];
        var candidates = new List<string> { title.Trim() };
        var separator = title.IndexOf(" - ", StringComparison.Ordinal);
        if (separator > 0)
        {
            var shortTitle = title[..separator].Trim();
            if (shortTitle.Length > 0 && !shortTitle.Equals(candidates[0], StringComparison.OrdinalIgnoreCase))
                candidates.Add(shortTitle);
        }
        return candidates;
    }

    internal static string RemoveParentheticalFileTags(string title)
    {
        var cleaned = Regex.Replace(title, @"\s*\([^()]*\)", " ",
            RegexOptions.CultureInvariant);
        return Regex.Replace(cleaned, @"\s+", " ", RegexOptions.CultureInvariant)
            .Trim(' ', '-', '_');
    }

    public async Task<MetadataSuggestion?> LookupAdfAsync(string adfPath, ScreenScraperAccess access,
        CancellationToken cancellationToken = default) =>
        await LookupAdfAsync(adfPath, access, ScreenScraperPreferences.Default, cancellationToken);

    public async Task<MetadataSuggestion?> LookupAdfAsync(string adfPath, ScreenScraperAccess access,
        ScreenScraperPreferences preferences, CancellationToken cancellationToken = default)
    {
        preferences.Validate();
        if (!Path.GetExtension(adfPath).Equals(".adf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ScreenScraper hash lookup currently supports ADF files only.", nameof(adfPath));
        var file = new FileInfo(adfPath);
        var bytes = await File.ReadAllBytesAsync(adfPath, cancellationToken);
        var sha1 = Convert.ToHexString(SHA1.HashData(bytes));
        var query = BaseQuery(access);
        query["systemeid"] = SystemId(adfPath);
        query["romtype"] = "rom";
        query["romnom"] = file.Name;
        query["romtaille"] = file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        query["crc"] = Crc32(bytes).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        query["md5"] = Convert.ToHexString(MD5.HashData(bytes));
        query["sha1"] = sha1;
        var document = await GetDocumentAsync(Endpoint, query, cancellationToken);
        if (document is null) return null;
        var match = ParseExactMatch(document, sha1, file.Length, preferences);
        if (match is null) return null;
        var game = document.Descendants().First(node => node.Name.LocalName == "jeu");
        match = await ResolvePublisherLogoAsync(match, game, access, cancellationToken);
        var fileName = AdfMetadataSuggester.SuggestFromFileName(Path.GetFileNameWithoutExtension(adfPath));
        if (fileName is null) return match;
        var missingTitle = string.IsNullOrWhiteSpace(match.Metadata.Title);
        var missingDate = string.IsNullOrWhiteSpace(match.Metadata.ReleaseDate);
        if (!missingTitle && !missingDate) return match;
        return match with
        {
            Metadata = match.Metadata with
            {
                Title = missingTitle ? fileName.Metadata.Title : match.Metadata.Title,
                ReleaseDate = missingDate ? fileName.Metadata.ReleaseDate : match.Metadata.ReleaseDate
            },
            Source = match.Source + "; missing title/date from file name"
        };
    }

    private static Dictionary<string, string> BaseQuery(ScreenScraperAccess access)
    {
        var query = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(access.UserId)) query["ssid"] = access.UserId.Trim();
        if (!string.IsNullOrEmpty(access.UserPassword)) query["sspassword"] = access.UserPassword;
        return query;
    }

    private static string SystemId(string path) =>
        Path.GetFileName(path).Contains("AGA", StringComparison.OrdinalIgnoreCase) ? "111" : "64";

    private static uint Crc32(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u);
        }
        return ~crc;
    }

    private async Task<XDocument?> GetDocumentAsync(string endpoint, Dictionary<string, string> query,
        CancellationToken cancellationToken)
    {
        await WaitForRateWindowAsync(cancellationToken);
        await _requestSlots.WaitAsync(cancellationToken);
        var uri = ProxyRoot + "/v1/screenscraper";
        Trace.WriteLine($"[ScreenScraperProxy] POST {uri}; endpoint={endpoint}; " +
            $"parameters={RedactedParameters(query)}");
        try
        {
            using var response = await httpClient.PostAsJsonAsync(uri,
                new Dictionary<string, object> { ["endpoint"] = endpoint, ["parameters"] = query },
                cancellationToken);
            Trace.WriteLine($"[ScreenScraperProxy] HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new IOException("ScreenScraper proxy rate limit reached; try again later.");
            if ((int)response.StatusCode == 430)
                throw new IOException("The ScreenScraper daily request quota has been reached.");
            if (!response.IsSuccessStatusCode)
                throw new IOException($"ScreenScraper proxy returned HTTP {(int)response.StatusCode}.");
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = XmlReader.Create(body, new XmlReaderSettings
            {
                Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 2_000_000
            });
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
            if (ParseUserInfo(document) is { } info) UserInfoUpdated?.Invoke(info);
            return document;
        }
        catch (HttpRequestException)
        { throw new IOException("ScreenScraper proxy request failed. Check the connection."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new IOException("ScreenScraper request timed out."); }
        catch (XmlException)
        { throw new IOException("ScreenScraper proxy did not return valid XML."); }
        finally { _requestSlots.Release(); }
    }

    private async Task WaitForRateWindowAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            lock (_rateLock)
            {
                var now = DateTimeOffset.UtcNow;
                while (_requestTimes.Count > 0 && now - _requestTimes.Peek() >= TimeSpan.FromMinutes(1))
                    _requestTimes.Dequeue();
                if (_requestTimes.Count < _maxRequestsPerMinute)
                {
                    _requestTimes.Enqueue(now);
                    return;
                }
                delay = TimeSpan.FromMinutes(1) - (now - _requestTimes.Peek()) + TimeSpan.FromMilliseconds(50);
            }
            await Task.Delay(delay, cancellationToken);
        }
    }

    internal static ScreenScraperUserInfo? ParseUserInfo(XDocument document)
    {
        var user = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "ssuser");
        if (user is null) return null;
        int Number(string name, int fallback = 0) => int.TryParse(Child(user, name), out var value) ? value : fallback;
        return new ScreenScraperUserInfo(Child(user, "id"), Math.Max(1, Number("maxthreads", 1)),
            Number("requeststoday"), Number("requestskotoday"),
            Math.Max(1, Number("maxrequestspermin", Number("maxrequestsperdmin", 20))),
            Math.Max(0, Number("maxrequestsperday")), Math.Max(0, Number("maxrequestskoperday")));
    }

    private static string RedactedParameters(Dictionary<string, string> query) =>
        string.Join(",", query.Select(pair => pair.Key + "=" +
            (pair.Key is "ssid" or "sspassword" ? "***" : pair.Value)));

    internal static MetadataSuggestion? ParseExactMatch(XDocument document, string sha1, long size,
        ScreenScraperPreferences? preferences = null)
    {
        var game = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "jeu");
        if (game is null) return null;
        var rom = game.Descendants().FirstOrDefault(node => node.Name.LocalName == "rom" &&
            Child(node, "romsha1")?.Equals(sha1, StringComparison.OrdinalIgnoreCase) == true &&
            Child(node, "romsize") == size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (rom is null) return null;
        return ParseGame(game, rom, "ScreenScraper (exact SHA-1 and size match)",
            preferences ?? ScreenScraperPreferences.Default);
    }

    private static MetadataSuggestion? ParseGame(XElement game, XElement? rom, string source,
        ScreenScraperPreferences preferences)
    {
        var regionPriority = new[] { preferences.Region, "eu", "wor", "us", "fr", "ss" }
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var languagePriority = new[] { preferences.Language, "de", "en", "fr", "es", "it" }
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var title = (Preferred(game, "noms", "nom", regionPriority) ?? Child(game, "nom"))?
            .Replace("ZZZ(notgame):", "", StringComparison.OrdinalIgnoreCase).Trim();
        var studio = rom is null ? null : Child(rom, "developpeur");
        studio ??= Child(game, "developpeur");
        var publisher = rom is null ? null : Child(rom, "editeur");
        publisher ??= Child(game, "editeur");
        var releaseDate = (rom is null ? null : Preferred(rom, "dates", "date", regionPriority)) ??
            Preferred(game, "dates", "date", regionPriority);
        var description = (rom is null ? null : Preferred(rom, "synopsis", "synopsis", languagePriority)) ??
            Preferred(game, "synopsis", "synopsis", languagePriority);
        var genre = (rom is null ? null : Preferred(rom, "genres", "genre", languagePriority)) ??
            Preferred(game, "genres", "genre", languagePriority);
        var rating = ParseRating(Child(game, "note"));
        var cover = MediaUrl(game, "box-2D", regionPriority);
        var screenshot = MediaUrl(game, "ss", regionPriority);
        var publisherLogo = CompanyMediaUrl(game, "editeurmedias",
            "editeurmedia_pictocouleur", "editeurmedia_pictomonochrome");
        Trace.WriteLine($"[PublisherLogo] ScreenScraper publisher='{publisher ?? "(none)"}', " +
            $"media={LogoKind(publisherLogo)}");
        var category = (rom is null ? null : Child(rom, "demo")) == "1" ? SoftwareCategory.Demo :
            Child(game, "notgame")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
                ? SoftwareCategory.Unknown : SoftwareCategory.Game;
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(studio) &&
            string.IsNullOrWhiteSpace(publisher) && string.IsNullOrWhiteSpace(description)) return null;
        return new MetadataSuggestion(new SoftwareMetadata(title, studio, category, releaseDate,
            publisher, description, cover, screenshot, Genre: genre, PublisherLogoUrl: publisherLogo,
            Rating: rating),
            source);
    }

    private static double? ParseRating(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var score) && score is >= 0 and <= 20
            ? score / 4d : null;

    private static string? Preferred(XElement parent, string group, string item, string[] priorities)
    {
        var container = parent.Elements().FirstOrDefault(node => node.Name.LocalName == group);
        if (container is null) return null;
        var values = container.Elements().Where(node => node.Name.LocalName == item ||
            node.Name.LocalName.StartsWith(item + "_", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var region in priorities)
        {
            var value = values.FirstOrDefault(node =>
                ((string?)node.Attribute("region") ?? (string?)node.Attribute("langue") ??
                 (string?)node.Attribute("id") ?? node.Name.LocalName[(item.Length)..].TrimStart('_'))
                .Equals(region, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return values.Select(node => node.Value.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? MediaUrl(XElement game, string type, string[] regions)
    {
        var media = game.Elements().FirstOrDefault(node => node.Name.LocalName == "medias");
        if (media is null) return null;
        var covers = media.Elements().Where(node =>
            ((string?)node.Attribute("type") ?? node.Name.LocalName).Equals(type, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var selected = regions.Select(region => covers.FirstOrDefault(node =>
            string.Equals((string?)node.Attribute("region"), region, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(node => node is not null) ?? covers.FirstOrDefault();
        var value = selected?.Value.Trim();
        if (!IsTrustedMediaUrl(value)) return null;
        var uri = new Uri(value!);
        return uri.ToString();
    }

    private static string? CompanyMediaUrl(XElement game, string containerName, params string[] names)
    {
        var container = game.Descendants().FirstOrDefault(node =>
            node.Name.LocalName.Equals(containerName, StringComparison.OrdinalIgnoreCase));
        if (container is null) return null;
        foreach (var name in names)
        {
            var node = container.Descendants().FirstOrDefault(candidate =>
                candidate.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            var value = ((string?)node?.Attribute("url") ?? node?.Value)?.Trim();
            if (IsTrustedMediaUrl(value)) return value;
        }
        var mediaCandidates = container.Descendants()
            .Select(media => new
            {
                Element = media,
                Kind = ((string?)media.Attribute("type") ?? (string?)media.Attribute("media") ??
                    media.Name.LocalName).ToLowerInvariant()
            })
            .Where(candidate => candidate.Kind.Contains("picto") || candidate.Kind.Contains("logo"))
            .OrderByDescending(candidate => candidate.Kind.Contains("couleur") ||
                candidate.Kind.Contains("color"));
        foreach (var candidate in mediaCandidates)
        {
            var value = ((string?)candidate.Element.Attribute("url") ?? candidate.Element.Value).Trim();
            if (IsTrustedMediaUrl(value)) return value;
        }
        return null;
    }

    private async Task<MetadataSuggestion> ResolvePublisherLogoAsync(MetadataSuggestion suggestion,
        XElement game, ScreenScraperAccess access, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(suggestion.Metadata.PublisherLogoUrl)) return suggestion;
        var publisher = game.Elements().FirstOrDefault(node => node.Name.LocalName == "editeur");
        var companyId = ((string?)publisher?.Attribute("id") ??
            (string?)publisher?.Attribute("companyid"))?.Trim();
        if (string.IsNullOrWhiteSpace(companyId) || !companyId.All(char.IsAsciiDigit)) return suggestion;
        var query = BaseQuery(access);
        query["companyid"] = companyId;
        query["media"] = "logo-couleur";
        query["outputformat"] = "png";
        using var response = await httpClient.PostAsJsonAsync(ProxyRoot + "/v1/media-token",
            new Dictionary<string, object>
            {
                ["endpoint"] = "mediaCompagnie.php", ["parameters"] = query
            }, cancellationToken);
        if (!response.IsSuccessStatusCode) return suggestion;
        var result = await response.Content.ReadFromJsonAsync<MediaTokenResponse>(cancellationToken);
        return Uri.TryCreate(result?.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? suggestion with { Metadata = suggestion.Metadata with { PublisherLogoUrl = uri.ToString() } }
            : suggestion;
    }

    private sealed record MediaTokenResponse(string Url);

    private static bool IsScreenScraperUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("screenscraper.fr", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".screenscraper.fr", StringComparison.OrdinalIgnoreCase));

    private static bool IsTrustedMediaUrl(string? value) => IsScreenScraperUrl(value) ||
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals(new Uri(ProxyRoot).Host, StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.Equals("/v1/media", StringComparison.Ordinal);

    private static string LogoKind(string? value) => string.IsNullOrWhiteSpace(value) ? "none" :
        value.Contains("monochrome", StringComparison.OrdinalIgnoreCase) ? "monochrome" :
        value.Contains("couleur", StringComparison.OrdinalIgnoreCase) ? "color" : "direct URL";

    private static string? Child(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(node => node.Name.LocalName == name)?.Value.Trim();
}

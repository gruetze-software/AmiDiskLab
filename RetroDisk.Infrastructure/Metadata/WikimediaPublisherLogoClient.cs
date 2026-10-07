using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;

namespace AmiDiskLab.Infrastructure.Metadata;

public sealed class WikimediaPublisherLogoClient(HttpClient httpClient)
{
    private const string Api = "https://www.wikidata.org/w/api.php";
    private const string CommonsApi = "https://commons.wikimedia.org/w/api.php";
    public async Task<string?> FindLogoAsync(string? publisher, string? preferredLanguage = null,
        string? softwareTitle = null, string? releaseYear = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return null;
        var name = publisher.Trim();
        Trace.WriteLine($"[PublisherLogo] Wikimedia lookup for publisher '{name}'.");
        var languages = new[] { preferredLanguage, "de", "fr", "en", "es", "it" }
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!string.IsNullOrWhiteSpace(preferredLanguage))
        {
            foreach (var language in languages)
            {
                var direct = await SearchWikipediaByPublisherAsync(language, name, softwareTitle,
                    releaseYear, cancellationToken);
                if (direct is not null) return direct;
            }
        }
        var searchUri = Api + "?action=wbsearchentities&format=json&formatversion=2&type=item" +
            "&language=en&uselang=en&limit=5&search=" + Uri.EscapeDataString(name);
        using var search = await GetJsonAsync(searchUri, cancellationToken);
        var results = Property(search.RootElement, "search");
        if (results.ValueKind != JsonValueKind.Array)
            return await SearchCommonsAsync(name, cancellationToken);
        var match = results.EnumerateArray()
            .Where(item => IsExactNameMatch(item, name))
            .OrderByDescending(IsSoftwareCompany)
            .FirstOrDefault();
        var id = Text(match, "id");
        Trace.WriteLine(string.IsNullOrWhiteSpace(id)
            ? $"[PublisherLogo] Wikidata has no exact entity for '{name}'."
            : $"[PublisherLogo] Wikidata matched '{name}' to {id} ({Text(match, "label")}).");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 20 || id[0] != 'Q' ||
            !id.AsSpan(1).ToArray().All(char.IsAsciiDigit))
            return await SearchCommonsAsync(name, cancellationToken);

        var entityUri = Api + "?action=wbgetentities&format=json&formatversion=2&props=claims%7Csitelinks&ids=" +
            Uri.EscapeDataString(id);
        using var entity = await GetJsonAsync(entityUri, cancellationToken);
        var entities = Property(entity.RootElement, "entities");
        var item = Property(entities, id);
        var claims = Property(item, "claims");
        var logos = Property(claims, "P154");
        if (logos.ValueKind == JsonValueKind.Array)
        {
            foreach (var claim in logos.EnumerateArray())
            {
                var value = Property(Property(Property(claim, "mainsnak"), "datavalue"), "value");
                if (value.ValueKind != JsonValueKind.String) continue;
                var fileName = value.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 500 ||
                    fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                var logo = "https://commons.wikimedia.org/wiki/Special:Redirect/file/" +
                    Uri.EscapeDataString(fileName) + "?width=512";
                Trace.WriteLine($"[PublisherLogo] Wikidata P154 logo: {logo}");
                return logo;
            }
        }
        var commons = await SearchCommonsAsync(name, cancellationToken);
        if (commons is not null) return commons;
        var sitelinks = Property(item, "sitelinks");
        foreach (var language in languages)
        {
            var articleTitle = Text(Property(sitelinks, language + "wiki"), "title");
            var articleImage = await SearchWikipediaPageImageAsync(language, articleTitle,
                cancellationToken);
            if (articleImage is not null) return articleImage;
        }
        return null;
    }

    private async Task<string?> SearchWikipediaByPublisherAsync(string language, string publisher,
        string? softwareTitle, string? releaseYear, CancellationToken cancellationToken)
    {
        var normalizedPublisher = NormalizeName(publisher);
        foreach (var searchTerm in new[]
        {
            publisher,
            publisher + " Software",
            publisher + " game developer",
            string.Join(' ', new[] { publisher, softwareTitle, releaseYear }
                .Where(value => !string.IsNullOrWhiteSpace(value)))
        }
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var uri = $"https://{language}.wikipedia.org/w/api.php" +
                "?action=query&format=json&formatversion=2&generator=search&gsrnamespace=0&gsrlimit=5" +
                "&prop=pageimages%7Cextracts&piprop=thumbnail&pithumbsize=512" +
                "&exintro=1&explaintext=1&exchars=800&gsrsearch=" + Uri.EscapeDataString(searchTerm);
            Trace.WriteLine($"[PublisherLogo] Direct {language}wiki search for '{searchTerm}'.");
            using var document = await GetJsonAsync(uri, cancellationToken);
            var pages = Property(Property(document.RootElement, "query"), "pages");
            if (pages.ValueKind != JsonValueKind.Array) continue;
            foreach (var page in pages.EnumerateArray())
            {
                var title = Text(page, "title");
                var normalizedTitle = NormalizeName(title ?? string.Empty);
                if (normalizedTitle != normalizedPublisher &&
                    normalizedTitle != normalizedPublisher + "software") continue;
                if (!HasVideoGameContext(Text(page, "extract")))
                {
                    Trace.WriteLine($"[PublisherLogo] Rejected {language}wiki article '{title}': " +
                        "no video-game company context.");
                    continue;
                }
                var imageUrl = Text(Property(page, "thumbnail"), "source");
                if (!IsWikimediaImage(imageUrl)) continue;
                var cleanUrl = CleanWikimediaImageUrl(imageUrl!);
                Trace.WriteLine($"[PublisherLogo] Direct {language}wiki selected '{title}': {cleanUrl}");
                return cleanUrl;
            }
        }
        Trace.WriteLine($"[PublisherLogo] Direct {language}wiki search has no verified publisher image.");
        return null;
    }

    private static bool HasVideoGameContext(string? text) =>
        !string.IsNullOrWhiteSpace(text) && new[]
        {
            "video game", "computer game", "game developer", "software developer",
            "computerspiel", "spielesoftware", "videospiel", "spieleentwickler",
            "jeu vidéo", "jeux vidéo", "développeur de jeux", "videojuego", "videogioco"
        }.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private async Task<string?> SearchCommonsAsync(string publisher, CancellationToken cancellationToken)
    {
        var uri = CommonsApi + "?action=query&format=json&formatversion=2&generator=search" +
            "&gsrnamespace=6&gsrlimit=10&prop=imageinfo&iiprop=url&iiurlwidth=512&gsrsearch=" +
            Uri.EscapeDataString(publisher + " logo");
        Trace.WriteLine($"[PublisherLogo] No usable Wikidata P154 logo; searching Commons for '{publisher}'.");
        using var document = await GetJsonAsync(uri, cancellationToken);
        var pages = Property(Property(document.RootElement, "query"), "pages");
        if (pages.ValueKind != JsonValueKind.Array) return null;
        var normalizedPublisher = NormalizeName(publisher);
        foreach (var page in pages.EnumerateArray())
        {
            var title = Text(page, "title");
            if (string.IsNullOrWhiteSpace(title) || !title.Contains("logo", StringComparison.OrdinalIgnoreCase) ||
                !NormalizeName(title).Contains(normalizedPublisher, StringComparison.Ordinal)) continue;
            var imageInfo = Property(page, "imageinfo");
            if (imageInfo.ValueKind != JsonValueKind.Array) continue;
            var first = imageInfo.EnumerateArray().FirstOrDefault();
            var imageUrl = Text(first, "thumburl") ?? Text(first, "url");
            if (IsWikimediaImage(imageUrl))
            {
                var cleanUrl = CleanWikimediaImageUrl(imageUrl!);
                Trace.WriteLine($"[PublisherLogo] Commons selected '{title}': {cleanUrl}");
                return cleanUrl;
            }
        }
        Trace.WriteLine($"[PublisherLogo] Commons found no usable logo for '{publisher}'.");
        return null;
    }

    private async Task<string?> SearchWikipediaPageImageAsync(string language, string? articleTitle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(articleTitle)) return null;
        var uri = $"https://{language}.wikipedia.org/w/api.php" +
            "?action=query&format=json&formatversion=2" +
            "&prop=pageimages&piprop=thumbnail&pithumbsize=512&titles=" +
            Uri.EscapeDataString(articleTitle);
        Trace.WriteLine($"[PublisherLogo] Checking {language}wiki article image for '{articleTitle}'.");
        using var document = await GetJsonAsync(uri, cancellationToken);
        var pages = Property(Property(document.RootElement, "query"), "pages");
        if (pages.ValueKind != JsonValueKind.Array) return null;
        foreach (var page in pages.EnumerateArray())
        {
            var imageUrl = Text(Property(page, "thumbnail"), "source");
            if (!IsWikimediaImage(imageUrl)) continue;
            var cleanUrl = CleanWikimediaImageUrl(imageUrl!);
            Trace.WriteLine($"[PublisherLogo] {language}wiki article image selected: {cleanUrl}");
            return cleanUrl;
        }
        Trace.WriteLine($"[PublisherLogo] {language}wiki has no usable article image for '{articleTitle}'.");
        return null;
    }

    private static string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsWikimediaImage(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("commons.wikimedia.org", StringComparison.OrdinalIgnoreCase));

    private static string CleanWikimediaImageUrl(string value)
    {
        var builder = new UriBuilder(value) { Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.ToString();
    }

    private async Task<JsonDocument> GetJsonAsync(string uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("AmiDiskLab/0.1 (publisher logo lookup)");
        Trace.WriteLine($"[Wikimedia] GET {uri}");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        Trace.WriteLine($"[Wikimedia] HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        if (response.StatusCode == HttpStatusCode.NotFound) return JsonDocument.Parse("{}");
        if (!response.IsSuccessStatusCode)
            throw new IOException($"Wikidata returned HTTP {(int)response.StatusCode}.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 32 },
            cancellationToken);
    }

    private static bool IsSoftwareCompany(JsonElement item)
    {
        var description = Text(item, "description");
        return description?.Contains("video game", StringComparison.OrdinalIgnoreCase) == true ||
            description?.Contains("software", StringComparison.OrdinalIgnoreCase) == true ||
            description?.Contains("publisher", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsExactNameMatch(JsonElement item, string name)
    {
        if (string.Equals(Text(item, "label"), name, StringComparison.OrdinalIgnoreCase)) return true;
        var match = Property(item, "match");
        return string.Equals(Text(match, "text"), name, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(Text(match, "type"), "alias", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Text(match, "type"), "label", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value : default;

    private static string? Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}

using AmiDiskLab.Core.Models;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AmiDiskLab.Infrastructure.Metadata;

public sealed class DemozooClient(HttpClient httpClient)
{
    private const string Api = "https://demozoo.org/api/v1/productions/";
    private static readonly Regex DiskTag = new(@"\s*\((?:disk\s*)?\d+(?:\s+of\s+\d+)?\)(?:\s*\([^)]*\))*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex YearAndAfter = new(@"(?:_|\s)(?:19|20)\d{2}(?:_|\s|$).*",
        RegexOptions.CultureInvariant);

    public static string SearchTitle(string fileName, string? savedTitle = null, string? diskSetTitle = null)
    {
        if (!string.IsNullOrWhiteSpace(savedTitle)) return savedTitle.Trim();
        var stem = diskSetTitle ?? Path.GetFileNameWithoutExtension(fileName);
        stem = YearAndAfter.Replace(stem, "");
        stem = DiskTag.Replace(stem, "");
        stem = stem.Replace('_', ' ');
        return Regex.Replace(stem, @"\s+", " ").Trim();
    }

    public async Task<IReadOnlyList<MetadataSuggestion>> SearchAsync(string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var byId = TryProductionId(query.Trim(), out var productionId);
        var uri = byId ? Api + productionId + "/" :
            Api + "?title=" + Uri.EscapeDataString(query.Trim()) +
            "&fields=id,title,author_nicks,author_affiliation_nicks,release_date,platforms,types,screenshots,demozoo_url";
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        if (!response.IsSuccessStatusCode)
            throw new IOException($"Demozoo returned HTTP {(int)response.StatusCode}.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
        { MaxDepth = 32 }, cancellationToken);
        var root = document.RootElement;
        if (byId)
        {
            var match = root.ValueKind == JsonValueKind.Object && IsAmiga(root) ? Parse(root, true) : null;
            return match is null ? [] : [match];
        }
        var results = root.ValueKind == JsonValueKind.Array ? root : Property(root, "results");
        if (results.ValueKind != JsonValueKind.Array) return [];
        return results.EnumerateArray().Take(40).Where(IsAmiga)
            .Select(item => Parse(item, false)).Where(match => match is not null)
            .Cast<MetadataSuggestion>().ToArray();
    }

    private static bool TryProductionId(string query, out int id)
    {
        id = 0;
        if (int.TryParse(query, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out id) && id > 0) return true;
        if (!Uri.TryCreate(query, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host is not ("demozoo.org" or "www.demozoo.org")) return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0].Equals("productions", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out id) && id > 0;
    }

    private static bool IsAmiga(JsonElement item) =>
        Property(item, "platforms") is { ValueKind: JsonValueKind.Array } platforms &&
        platforms.EnumerateArray().Any(platform =>
            Text(platform, "name")?.Contains("Amiga", StringComparison.OrdinalIgnoreCase) == true);

    private static MetadataSuggestion? Parse(JsonElement item, bool byId)
    {
        var title = Text(item, "title");
        if (string.IsNullOrWhiteSpace(title)) return null;
        var authors = Property(item, "author_nicks");
        var affiliations = Property(item, "author_affiliation_nicks");
        var group = Group(authors) ?? Group(affiliations);
        var types = Property(item, "types");
        var productionType = types.ValueKind == JsonValueKind.Array
            ? string.Join(", ", types.EnumerateArray().Select(type => Text(type, "name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))) : null;
        var screenshots = Property(item, "screenshots");
        var imageUrls = screenshots.ValueKind == JsonValueKind.Array
            ? screenshots.EnumerateArray().Select(image => Text(image, "standard_url") ??
                Text(image, "original_url") ?? Text(image, "thumbnail_url"))
                .Where(IsDemozooImage).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
        var coverUrl = imageUrls.FirstOrDefault();
        var screenshotUrl = imageUrls.Skip(1).FirstOrDefault() ?? coverUrl;
        var url = Text(item, "demozoo_url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var source) ||
            source.Scheme != Uri.UriSchemeHttps || source.Host != "demozoo.org") url = null;
        return new MetadataSuggestion(new SoftwareMetadata(title, group, SoftwareCategory.Demo,
            Text(item, "release_date"), null, null, coverUrl, screenshotUrl, group,
            productionType, url), $"Demozoo ({(byId ? "ID" : "title")} match; file not verified)" +
            (url is null ? string.Empty : $" · {url}"));
    }

    private static string? Group(JsonElement authors)
    {
        if (authors.ValueKind != JsonValueKind.Array) return null;
        return authors.EnumerateArray().Select(author => Property(author, "releaser"))
            .Where(releaser => releaser.ValueKind == JsonValueKind.Object &&
                Property(releaser, "is_group").ValueKind == JsonValueKind.True)
            .Select(releaser => Text(releaser, "name")).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
    }

    private static bool IsDemozooImage(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host == "demozoo.org" || uri.Host.EndsWith(".demozoo.org", StringComparison.OrdinalIgnoreCase));

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value : default;

    private static string? Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}

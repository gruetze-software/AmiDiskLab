using AmiDiskLab.Core.Models;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace AmiDiskLab.Infrastructure.FileSystem;

public static class AdfMetadataSuggester
{
    private static readonly Regex Year = new(@"^(?:19|20)(?:\d{2}|xx)(?:-\d{2}-\d{2})?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SuffixTags = new(StringComparer.OrdinalIgnoreCase)
    { "cr", "h", "t", "f", "PAL", "NTSC", "AGA", "OCS", "ECS", "US", "DE", "FR", "IT", "PD" };

    public static Task<MetadataSuggestion?> SuggestAsync(string adfPath, CancellationToken token = default) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var rp9 = ReadCompanionRp9(adfPath, token);
            return rp9 ?? SuggestFromFileName(Path.GetFileNameWithoutExtension(adfPath));
        }, token);

    internal static MetadataSuggestion? SuggestFromFileName(string stem)
    {
        var parts = stem.Split('_', StringSplitOptions.None);
        var yearIndex = Array.FindIndex(parts, Year.IsMatch);
        if (yearIndex <= 0) return null;
        var titleParts = parts[..yearIndex].Where(part => !Regex.IsMatch(part, @"^v\d", RegexOptions.IgnoreCase))
            .ToArray();
        if (titleParts.Length == 0) return null;
        var title = string.Join(" ", titleParts).Replace("  ", " / ").Trim();
        var demo = parts.Any(part => part.StartsWith("demo-", StringComparison.OrdinalIgnoreCase));
        title = Regex.Replace(title, @"\s+demo-(?:playable|rolling)$", string.Empty, RegexOptions.IgnoreCase);
        var studioParts = new List<string>();
        for (var i = yearIndex + 1; i < parts.Length; i++)
        {
            if (SuffixTags.Contains(parts[i]) || parts[i].StartsWith("demo-", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("Amiga", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length &&
                parts[i + 1].Equals("Format", StringComparison.OrdinalIgnoreCase)) break;
            studioParts.Add(parts[i]);
        }
        var studio = studioParts.Count == 0 ? null : string.Join(" ", studioParts).Trim();
        var category = demo ? SoftwareCategory.Demo : SoftwareCategory.Game;
        return new MetadataSuggestion(new SoftwareMetadata(title, studio, category,
            parts[yearIndex][..4]), "file name (unverified)");
    }

    private static MetadataSuggestion? ReadCompanionRp9(string adfPath, CancellationToken token)
    {
        var folder = Path.GetDirectoryName(adfPath);
        if (folder is null) return null;
        foreach (var rp9Path in Directory.EnumerateFiles(folder, "*.rp9"))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var zip = ZipFile.OpenRead(rp9Path);
                var manifest = zip.GetEntry("rp9-manifest.xml");
                if (manifest is null || manifest.Length > 256_000) continue;
                using var stream = manifest.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var document = XDocument.Load(reader);
                foreach (var application in document.Descendants().Where(element => element.Name.LocalName == "application"))
                {
                    var media = application.Descendants().Where(element => element.Name.LocalName == "floppy")
                        .Select(element => Path.GetFileName(element.Value.Trim()));
                    if (!media.Any(name => name.Equals(Path.GetFileName(adfPath), StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var description = application.Descendants().FirstOrDefault(element => element.Name.LocalName == "description");
                    if (description is null) continue;
                    var title = description.Elements().FirstOrDefault(element => element.Name.LocalName == "title")?.Value.Trim();
                    var type = description.Elements().FirstOrDefault(element => element.Name.LocalName == "type")?.Value.Trim();
                    var studio = description.Elements().FirstOrDefault(element => element.Name.LocalName == "entity" &&
                        (string?)element.Attribute("type") == "developer")?.Value.Trim();
                    var publisher = description.Elements().FirstOrDefault(element => element.Name.LocalName == "entity" &&
                        (string?)element.Attribute("type") == "publisher")?.Value.Trim();
                    var releaseDate = description.Elements().FirstOrDefault(element => element.Name.LocalName == "year")?.Value.Trim();
                    var category = type?.ToLowerInvariant() switch
                    {
                        "game" => SoftwareCategory.Game,
                        "demo" => SoftwareCategory.Demo,
                        "application" or "utility" => SoftwareCategory.Program,
                        _ => SoftwareCategory.Unknown
                    };
                    return new MetadataSuggestion(new SoftwareMetadata(title, studio, category,
                        releaseDate, publisher),
                        $"RP9: {Path.GetFileName(rp9Path)}");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or XmlException)
            { /* A damaged companion is not authoritative; try the next one or the file name. */ }
        }
        return null;
    }
}

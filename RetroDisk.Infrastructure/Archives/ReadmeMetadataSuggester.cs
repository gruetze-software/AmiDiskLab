using AmiDiskLab.Core.Models;
using Amiga.FileFormats.LHA;
using System.Text;
using System.Text.RegularExpressions;

namespace AmiDiskLab.Infrastructure.Archives;

public static class ReadmeMetadataSuggester
{
    public static Task<SoftwareMetadata?> SuggestAsync(string archivePath, CancellationToken token = default) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var files = LHAReader.LoadLHAFile(archivePath).GetAllFiles();
            token.ThrowIfCancellationRequested();
            var readme = files.Where(file => file.Data.Length <= 256_000 &&
                    Regex.IsMatch(Path.GetFileName(file.Path.Split('\0')[0]),
                        @"^readme(?:\.txt)?$", RegexOptions.IgnoreCase))
                .OrderBy(file => file.Path.Count(c => c is '/' or '\\'))
                .FirstOrDefault();
            return readme is null ? null : Parse(Encoding.Latin1.GetString(readme.Data));
        }, token);

    internal static SoftwareMetadata? Parse(string text)
    {
        var line = Regex.Match(text,
            @"(?im)^\s*This\s+(?:install|patch)\s+applies\s+to\s+(?:the\s+)?(?:\w+\s+)?[""“]([^""”\r\n]+)[""”]([^\r\n]*)");
        string? title = line.Success ? line.Groups[1].Value.Trim() : null;
        string? studio = null;
        if (line.Success)
        {
            var credit = Regex.Match(line.Groups[2].Value,
                @"(?:©|\(c\)|copyright)\s*(?:19|20)\d{2}\s+([^.\r\n]+)", RegexOptions.IgnoreCase);
            if (credit.Success)
            {
                var candidate = credit.Groups[1].Value.Trim().TrimEnd('-', ' ');
                if (!candidate.Contains('/') && !candidate.Contains(',') && candidate.Length <= 100)
                    studio = candidate;
            }
        }
        var type = Regex.Match(text, @"(?im)^\s*Type:\s*([^\r\n]+)").Groups[1].Value;
        var category = type.StartsWith("demo/", StringComparison.OrdinalIgnoreCase) ? SoftwareCategory.Demo :
            type.StartsWith("game/", StringComparison.OrdinalIgnoreCase) ? SoftwareCategory.Game :
            type.StartsWith("util/", StringComparison.OrdinalIgnoreCase) ? SoftwareCategory.Program :
            SoftwareCategory.Unknown;
        if (category == SoftwareCategory.Unknown && line.Success &&
            Regex.IsMatch(text, @"(?im)^\s*The (?:installed )?game requires\b"))
            category = SoftwareCategory.Game;
        if (title is null && studio is null && category == SoftwareCategory.Unknown) return null;
        return new SoftwareMetadata(title, studio, category);
    }
}

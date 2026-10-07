using System.Text.RegularExpressions;

namespace AmiDiskLab.Core.Models;

public static class DiskSetGrouper
{
    private static readonly Regex DiskSuffix = new(
        @"^(?<title>.+?)\s*(?:\((?:(?:disk|disc)\s*)?(?<disk>[A-Z]|\d{1,2})(?:\s+of\s+(?<total>\d{1,2}))?\)(?:\s*\([^()]{1,40}\))*|[-_ ](?:disk|disc)\s*(?<disk>[A-Z]|\d{1,2})|[-_ ](?<disk>\d{1,2})\s*-\s*(?<total>\d{1,2})|(?<compact>[-_ ](?<disk>\d{1,2}))|(?<!\d)(?<compact>(?<disk>\d{2})))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? BaseTitle(string fileName)
    {
        var match = DiskSuffix.Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success && !match.Groups["compact"].Success
            ? match.Groups["title"].Value.TrimEnd(' ', '-', '_') : null;
    }

    public static IReadOnlyList<RetroSoftwareItem> Group(IReadOnlyList<RetroSoftwareItem> items)
    {
        var groups = items.Where(item => item.Type == SoftwareItemType.DiskImage)
            .Select(item => (Item: item, Match: DiskSuffix.Match(Path.GetFileNameWithoutExtension(item.FileName))))
            .Where(entry => entry.Match.Success)
            .GroupBy(entry => (Folder: Path.GetDirectoryName(entry.Item.FullPath) ?? string.Empty,
                Format: entry.Item.Format, Title: entry.Match.Groups["title"].Value.TrimEnd(' ', '-', '_')),
                new DiskKeyComparer())
            .Select(group => group.ToList())
            .Where(IsValidGroup)
            .ToList();

        var membership = new Dictionary<RetroSoftwareItem, List<(RetroSoftwareItem Item, Match Match)>>();
        foreach (var group in groups)
            foreach (var entry in group) membership[entry.Item] = group;

        var result = new List<RetroSoftwareItem>();
        var emitted = new HashSet<List<(RetroSoftwareItem Item, Match Match)>>();
        foreach (var item in items)
        {
            if (!membership.TryGetValue(item, out var group)) { result.Add(item); continue; }
            if (!emitted.Add(group)) continue;
            var ordered = group.OrderBy(entry => DiskOrder(entry.Match.Groups["disk"].Value)).ToList();
            var first = ordered[0].Item;
            result.Add(new RetroSoftwareItem
            {
                FileName = first.FileName, FullPath = first.FullPath, RelativePath = first.RelativePath,
                FileSize = ordered.Sum(entry => entry.Item.FileSize), Format = first.Format,
                Type = first.Type, Sha256 = first.Sha256,
                IsDuplicate = ordered.Any(entry => entry.Item.IsDuplicate),
                DiskSetTitle = ordered[0].Match.Groups["title"].Value.TrimEnd(' ', '-', '_'),
                DiskPaths = ordered.Select(entry => entry.Item.FullPath).ToArray()
            });
        }
        return result;
    }

    private static int DiskOrder(string label) => int.TryParse(label, out var number)
        ? number : char.ToUpperInvariant(label[0]) - 'A' + 1;

    private static bool IsValidGroup(List<(RetroSoftwareItem Item, Match Match)> group)
    {
        if (group.Count < 2) return false;
        var labels = group.Select(entry => entry.Match.Groups["disk"].Value).ToArray();
        if (labels.Select(label => int.TryParse(label, out _)).Distinct().Count() != 1 ||
            labels.Select(DiskOrder).Distinct().Count() != group.Count) return false;
        var compact = group.Select(entry => entry.Match.Groups["compact"].Success).Distinct().ToArray();
        if (compact.Length > 1) return false;
        if (compact[0])
        {
            var numbers = labels.Select(DiskOrder).Order().ToArray();
            if (!numbers.SequenceEqual(Enumerable.Range(1, group.Count))) return false;
        }
        var totals = group.Select(entry => entry.Match.Groups["total"].Value)
            .Where(value => value.Length > 0).Distinct().ToArray();
        if (totals.Length > 1) return false;
        return totals.Length == 0 || int.TryParse(totals[0], out var total) &&
            total >= group.Count && labels.All(label => DiskOrder(label) <= total);
    }

    private sealed class DiskKeyComparer : IEqualityComparer<(string Folder, SoftwareFormat Format, string Title)>
    {
        public bool Equals((string Folder, SoftwareFormat Format, string Title) x,
            (string Folder, SoftwareFormat Format, string Title) y) =>
            x.Format == y.Format && StringComparer.OrdinalIgnoreCase.Equals(x.Folder, y.Folder) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Title, y.Title);

        public int GetHashCode((string Folder, SoftwareFormat Format, string Title) key) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Folder), key.Format,
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Title));
    }
}

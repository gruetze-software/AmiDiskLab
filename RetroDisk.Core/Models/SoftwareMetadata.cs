namespace AmiDiskLab.Core.Models;

public enum SoftwareCategory
{
    Unknown,
    Game,
    Demo,
    Program,
    Music,
    Compilation,
    DiskMagazine,
    Other
}

public sealed record SoftwareMetadata(string? Title = null, string? Studio = null,
    SoftwareCategory Category = SoftwareCategory.Unknown, string? ReleaseDate = null,
    string? Publisher = null, string? Description = null, string? CoverUrl = null,
    string? ScreenshotUrl = null, string? SceneGroup = null, string? ProductionType = null,
    string? SourceUrl = null, string? Genre = null, string? PublisherLogoUrl = null,
    double? Rating = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Category) || Title?.Length > 200 || Studio?.Length > 200 ||
            Publisher?.Length > 200 || Description?.Length > 10_000 || ReleaseDate?.Length > 20 ||
            CoverUrl?.Length > 2_000 || ScreenshotUrl?.Length > 2_000 ||
            SceneGroup?.Length > 200 || ProductionType?.Length > 100 || SourceUrl?.Length > 2_000 ||
            Genre?.Length > 200 || PublisherLogoUrl?.Length > 2_000 ||
            Rating is < 0 or > 5 || double.IsNaN(Rating ?? 0) || double.IsInfinity(Rating ?? 0))
            throw new ArgumentException("Invalid software metadata.");
    }
}

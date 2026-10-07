namespace AmiDiskLab.Infrastructure.Metadata;

public sealed record ScreenScraperPreferences(string Language = "de", string Region = "eu")
{
    public static ScreenScraperPreferences Default { get; } = new();

    public void Validate()
    {
        if (Language is not ("de" or "en" or "fr" or "es" or "it") ||
            Region is not ("eu" or "us" or "jp" or "wor"))
            throw new ArgumentException("Unsupported ScreenScraper language or region.");
    }
}

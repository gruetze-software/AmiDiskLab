using AmiDiskLab.Infrastructure.Metadata;
using System;
using System.IO;
using System.Text.Json;

namespace AmiDiskLab.App;

internal static class ScreenScraperPreferencesStore
{
    private static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmiDiskLab", "screenscraper-preferences.json");

    public static ScreenScraperPreferences Load()
    {
        try
        {
            if (!File.Exists(PathName)) return ScreenScraperPreferences.Default;
            var value = JsonSerializer.Deserialize<ScreenScraperPreferences>(File.ReadAllText(PathName));
            value?.Validate();
            return value ?? ScreenScraperPreferences.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return ScreenScraperPreferences.Default; }
    }

    public static void Save(ScreenScraperPreferences value)
    {
        value.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var temporary = PathName + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, PathName, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

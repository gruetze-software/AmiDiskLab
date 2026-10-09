using Avalonia;
using Avalonia.Styling;
using System;
using System.IO;
using System.Text.Json;

namespace AmiDiskLab.App;

public enum AppTheme { System, Dark, Light }

public sealed record UiPreferences(AppTheme Theme)
{
    public static UiPreferences Default { get; } = new(AppTheme.System);
}

internal static class UiPreferencesStore
{
    private static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmiDiskLab", "ui-preferences.json");

    public static UiPreferences Load()
    {
        try
        {
            if (!File.Exists(PathName)) return UiPreferences.Default;
            var value = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(PathName));
            return value is not null && Enum.IsDefined(value.Theme) ? value : UiPreferences.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return UiPreferences.Default;
        }
    }

    public static void Save(UiPreferences value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, JsonSerializer.Serialize(value));
    }
}

internal static class ThemeManager
{
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }
}
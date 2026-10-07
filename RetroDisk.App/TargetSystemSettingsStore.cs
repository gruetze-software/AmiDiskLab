using AmiDiskLab.Core.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AmiDiskLab.App;

internal static class TargetSystemSettingsStore
{
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmiDiskLab", "target-system.json");

    public static AmigaTargetSystem Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return AmigaTargetSystem.Default;
            var target = JsonSerializer.Deserialize<AmigaTargetSystem>(File.ReadAllText(SettingsPath));
            target?.Validate();
            return target ?? AmigaTargetSystem.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException)
        {
            Trace.WriteLine($"Could not load target-system settings: {ex}");
            return AmigaTargetSystem.Default;
        }
    }

    public static void Save(AmigaTargetSystem target)
    {
        target.Validate();
        var folder = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(folder);
        var temporary = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(target));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

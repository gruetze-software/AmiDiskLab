using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace AmiDiskLab.App;

internal sealed record PublisherLogoEntry(string Path, bool IsColor);

internal static class PublisherLogoStore
{
    private static readonly object Sync = new();
    private static readonly string StorePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmiDiskLab", "publisher-logos.json");
    private static readonly Dictionary<string, PublisherLogoEntry> Entries = Load();

    public static PublisherLogoEntry? Get(string? publisher)
        => GetByKey(publisher, studio: false) ?? GetByKey(publisher, studio: true) ??
            GetEquivalent(publisher);

    public static PublisherLogoEntry? GetStudio(string? studio)
        => GetByKey(studio, studio: true) ?? GetByKey(studio, studio: false) ??
            GetEquivalent(studio);

    private static PublisherLogoEntry? GetEquivalent(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var identity = CompanyIdentity(name);
        if (identity.Length < 3) return null;
        lock (Sync)
        {
            var matches = Entries
                .Where(pair => File.Exists(pair.Value.Path) &&
                    CompanyIdentity(NamePart(pair.Key)) == identity)
                .Select(pair => pair.Value)
                .OrderByDescending(entry => entry.IsColor)
                .ToArray();
            if (matches.Length == 0) return null;
            Trace.WriteLine($"[PublisherLogo] Reusing equivalent company logo for '{name}'.");
            return matches[0];
        }
    }

    private static string NamePart(string key)
    {
        var separator = key.IndexOf(':');
        return separator >= 0 ? key[(separator + 1)..] : key;
    }

    private static string CompanyIdentity(string value)
    {
        var identity = new string(value.Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant).ToArray());
        foreach (var suffix in new[]
        {
            "SOFTWAREDEVELOPMENTS", "SOFTWAREDEVELOPMENT", "ENTERTAINMENT",
            "INTERACTIVE", "DEVELOPMENTS", "DEVELOPMENT", "SOFTWARE",
            "CORPORATION", "LIMITED", "GAMES", "LTD", "INC"
        })
        {
            if (identity.EndsWith(suffix, StringComparison.Ordinal) &&
                identity.Length - suffix.Length >= 3)
                identity = identity[..^suffix.Length];
        }
        return identity;
    }

    private static PublisherLogoEntry? GetByKey(string? name, bool studio)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (Sync)
        {
            if (Entries.TryGetValue(Key(name, studio), out var entry) && File.Exists(entry.Path))
                return entry;
            if (!studio && Entries.TryGetValue(name.Trim().ToUpperInvariant(), out var legacy) &&
                File.Exists(legacy.Path))
            {
                Entries[Key(name, studio: false)] = legacy;
                Save();
                return legacy;
            }
            return null;
        }
    }

    public static void ImportLegacy(string? publisher, string? path)
    {
        if (string.IsNullOrWhiteSpace(publisher) || string.IsNullOrWhiteSpace(path) ||
            !System.IO.Path.IsPathFullyQualified(path) || !File.Exists(path)) return;
        lock (Sync)
        {
            var key = Key(publisher, studio: false);
            if (Entries.ContainsKey(key)) return;
            Entries[key] = new PublisherLogoEntry(System.IO.Path.GetFullPath(path), false);
            Save();
        }
    }

    public static void Set(string? publisher, string path, bool isColor)
        => SetByKey(publisher, path, isColor, studio: false);

    public static void SetStudio(string? studio, string path, bool isColor)
        => SetByKey(studio, path, isColor, studio: true);

    private static void SetByKey(string? name, string path, bool isColor, bool studio)
    {
        if (string.IsNullOrWhiteSpace(name) || !System.IO.Path.IsPathFullyQualified(path) ||
            !File.Exists(path)) return;
        lock (Sync)
        {
            var key = Key(name, studio);
            if (Entries.TryGetValue(key, out var current) && current.IsColor && !isColor) return;
            Entries[key] = new PublisherLogoEntry(System.IO.Path.GetFullPath(path), isColor);
            Save();
        }
    }

    public static void Remove(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return;
        lock (Sync)
        {
            var removed = Entries.Remove(Key(publisher, studio: false));
            removed |= Entries.Remove(publisher.Trim().ToUpperInvariant());
            if (!removed) return;
            Save();
        }
    }

    public static void RemoveStudio(string? studio)
    {
        if (string.IsNullOrWhiteSpace(studio)) return;
        lock (Sync)
        {
            if (!Entries.Remove(Key(studio, studio: true))) return;
            Save();
        }
    }

    private static string Key(string name, bool studio) =>
        (studio ? "STUDIO:" : "PUBLISHER:") + name.Trim().ToUpperInvariant();

    private static Dictionary<string, PublisherLogoEntry> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new(StringComparer.OrdinalIgnoreCase);
            return JsonSerializer.Deserialize<Dictionary<string, PublisherLogoEntry>>(
                File.ReadAllText(StorePath)) is { } entries
                ? new(entries, StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.WriteLine($"[PublisherLogo] Could not load central logo index: {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath)!);
            var temporary = StorePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Entries));
            File.Move(temporary, StorePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Trace.WriteLine($"[PublisherLogo] Could not save central logo index: {ex.Message}"); }
    }
}

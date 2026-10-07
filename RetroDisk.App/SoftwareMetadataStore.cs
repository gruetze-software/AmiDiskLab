using AmiDiskLab.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AmiDiskLab.App;

public sealed class SoftwareMetadataStore
{
    private readonly string _path;
    private readonly Dictionary<string, SoftwareMetadata> _entries = new(StringComparer.OrdinalIgnoreCase);

    public SoftwareMetadataStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmiDiskLab", "metadata.json");
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, SoftwareMetadata>>(File.ReadAllText(_path));
            if (loaded is null) return;
            foreach (var (key, value) in loaded)
            {
                value.Validate();
                _entries[Path.GetFullPath(key)] = value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _entries.Clear();
            Trace.WriteLine($"Could not load software metadata: {ex}");
        }
    }

    public SoftwareMetadata Get(string filePath) =>
        _entries.TryGetValue(Path.GetFullPath(filePath), out var metadata) ? metadata : new();

    public void Set(string filePath, SoftwareMetadata metadata)
        => SetMany([filePath], metadata);

    public void SetMany(IEnumerable<string> filePaths, SoftwareMetadata metadata)
    {
        metadata.Validate();
        var keys = filePaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var previous = keys.ToDictionary(key => key,
            key => _entries.TryGetValue(key, out var value) ? value : null,
            StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys) _entries[key] = metadata;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(_entries));
                File.Move(temporary, _path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch
        {
            foreach (var (key, value) in previous)
            {
                if (value is null) _entries.Remove(key);
                else _entries[key] = value;
            }
            throw;
        }
    }
}

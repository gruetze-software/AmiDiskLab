using System;
using System.IO;
using System.Text.Json;

namespace AmiDiskLab.App;

public sealed class LastFolderStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmiDiskLab", "last-folder.json");

    public string? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var value = JsonSerializer.Deserialize<LastFolder>(File.ReadAllText(_path))?.Path;
            return !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value) ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return null; }
    }

    public void Save(string folderPath)
    {
        if (!Path.IsPathFullyQualified(folderPath) || !Directory.Exists(folderPath))
            throw new DirectoryNotFoundException("The selected folder no longer exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new LastFolder(folderPath)));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record LastFolder(string Path);
}

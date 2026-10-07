using AmiDiskLab.Core.Services;
using AmiDiskLab.Infrastructure.FileSystem;
using Amiga.FileFormats.LHA;

namespace AmiDiskLab.Infrastructure.Archives;

public sealed class LhaArchiveExtractor : IArchiveExtractor
{
    public Task ExtractAsync(string archivePath, string destinationFolder,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(archivePath)) throw new FileNotFoundException("LHA archive was not found.", archivePath);
        PathSafety.RejectLinks(destinationFolder);
        var archive = LHAReader.LoadLHAFile(archivePath);
        cancellationToken.ThrowIfCancellationRequested();
        var files = archive.GetAllFiles().Select(file =>
            (Path: GetSafeTargetPath(destinationFolder, string.IsNullOrWhiteSpace(file.Path) ? file.Name : file.Path),
             Data: file.Data)).ToList();
        var directories = archive.GetAllEmptyDirectories()
            .Where(directory => !string.IsNullOrWhiteSpace(directory.Path))
            .Select(directory => GetSafeTargetPath(destinationFolder, directory.Path)).ToList();
        ValidatePathCasing(destinationFolder, files.Select(file => file.Path).Concat(directories));
        // Reject collisions consistently, including on case-sensitive host filesystems.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files.Select(file => file.Path).Concat(directories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!names.Add(path)) throw new InvalidDataException($"Archive contains colliding paths: {path}");
            PathSafety.RejectLinks(path);
        }
        var fileNames = files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in names)
            for (var parent = Path.GetDirectoryName(path); parent is not null && PathSafety.IsWithin(destinationFolder, parent);
                parent = Path.GetDirectoryName(parent))
                if (fileNames.Contains(parent)) throw new InvalidDataException($"Archive file is also used as a directory: {parent}");
        foreach (var file in files)
            if (File.Exists(file.Path) || Directory.Exists(file.Path))
                throw new IOException($"Extraction would overwrite an existing entry: {file.Path}");
        foreach (var directory in directories)
            if (File.Exists(directory)) throw new IOException($"A file already occupies the directory path: {directory}");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationFolder);
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PathSafety.RejectLinks(directory);
            Directory.CreateDirectory(directory);
        }
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PathSafety.RejectLinks(file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
            await using var output = new FileStream(file.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous);
            await output.WriteAsync(file.Data, cancellationToken);
        }
    }, cancellationToken);

    internal static void ValidatePathCasing(string root, IEnumerable<string> paths)
    {
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            for (var current = path; current is not null && PathSafety.IsWithin(root, current);
                current = Path.GetDirectoryName(current))
            {
                if (canonical.TryGetValue(current, out var prior) && prior != current)
                    throw new InvalidDataException($"Archive paths differ only by case: {prior} / {current}");
                canonical[current] = current;
            }
        }
    }

    internal static string GetSafeTargetPath(string destinationFolder, string relativePath)
    {
        var nullIndex = relativePath.IndexOf('\0');
        if (nullIndex >= 0)
        {
            // Some LHA entries expose bytes beyond the C-string terminator.
            relativePath = relativePath[..nullIndex];
        }
        relativePath = relativePath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith('/') || relativePath.Contains(':'))
            throw new InvalidDataException($"Archive contains an invalid path: {relativePath}");
        var segments = relativePath.TrimEnd('/').Split('/');
        foreach (var segment in segments)
        {
            var stem = segment.Split('.')[0];
            if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c)) ||
                new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                    stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9'))
                throw new InvalidDataException($"Archive contains an unsafe path component: {segment}");
        }
        var root = Path.GetFullPath(destinationFolder);
        var target = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (target == root || !PathSafety.IsWithin(root, target))
            throw new InvalidDataException($"Archive path escapes the destination: {relativePath}");
        return target;
    }
}

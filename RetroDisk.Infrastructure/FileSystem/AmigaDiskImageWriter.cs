using Directory = System.IO.Directory;
using File = System.IO.File;
using AmiDiskLab.Core.Services;
using Hst.Amiga.FileSystems.FastFileSystem;
using Hst.Amiga.FileSystems.FastFileSystem.Blocks;
using System.Diagnostics;
using System.Text;

namespace AmiDiskLab.Infrastructure.FileSystem;

public sealed class AmigaDiskImageWriter : IAmigaDiskImageWriter
{
    private const int BlockSize = 512;
    private const int BlockCount = 1760;
    private const int AdfSize = BlockSize * BlockCount;

    public Task CreateBlankAdfAsync(string outputPath, string volumeName,
        CancellationToken cancellationToken = default) =>
        CreateImageAsync(outputPath, volumeName, (_, _) => Task.CompletedTask, cancellationToken);

    public Task CreateAdfWithFileAsync(string outputPath, string volumeName, string fileName,
        byte[] fileData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileData);
        ValidateName(fileName);
        CheckCapacity(4 + FileBlocks(fileData.LongLength));
        return CreateImageAsync(outputPath, volumeName, async (volume, token) =>
        {
            await using var source = new MemoryStream(fileData, writable: false);
            await WriteFileAsync(volume, fileName, source, fileData.Length, token);
        }, cancellationToken);
    }

    public Task CreateAdfFromFolderAsync(string sourceFolder, string outputPath, string volumeName,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException($"Source folder was not found: {sourceFolder}");
        var source = Path.GetFullPath(sourceFolder);
        var output = Path.GetFullPath(outputPath);
        if (PathSafety.IsWithin(source, output))
            throw new IOException("The output ADF must be outside the source folder.");
        PathSafety.RejectLinks(source);
        long blocks = 4; // Boot blocks, root and bitmap.
        var tree = InspectDirectory(source, ref blocks, cancellationToken);
        await CreateImageAsync(output, volumeName,
            (volume, token) => WriteDirectoryAsync(volume, tree, "/", token), cancellationToken);
    }, cancellationToken);

    public Task CreateBootableA500AdfFromFolderAsync(string sourceFolder, string outputPath,
        string volumeName, string executableRelativePath, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(executableRelativePath);
            if (!Directory.Exists(sourceFolder)) throw new DirectoryNotFoundException(sourceFolder);
            var source = Path.GetFullPath(sourceFolder);
            var output = Path.GetFullPath(outputPath);
            if (PathSafety.IsWithin(source, output))
                throw new IOException("The output ADF must be outside the source folder.");
            PathSafety.RejectLinks(source);
            var relative = ValidateStartupPath(executableRelativePath);
            var executable = Path.GetFullPath(Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!PathSafety.IsWithin(source, executable) || !File.Exists(executable))
                throw new FileNotFoundException("The selected Amiga executable does not exist in the source folder.", executable);
            PathSafety.RejectLinks(executable);
            if (!await AmigaExecutable.IsHunkFileAsync(executable, cancellationToken))
                throw new InvalidDataException("The selected file is not an AmigaDOS Hunk executable.");

            long blocks = 4;
            var tree = InspectDirectory(source, ref blocks, cancellationToken, ofs: true);
            var scriptDirectory = tree.Children.SingleOrDefault(child => child.Name.Equals("S", StringComparison.OrdinalIgnoreCase));
            if (tree.Files.Any(file => Path.GetFileName(file.Path).Equals("S", StringComparison.OrdinalIgnoreCase)) ||
                scriptDirectory?.Files.Any(file => Path.GetFileName(file.Path).Equals("Startup-Sequence", StringComparison.OrdinalIgnoreCase)) == true)
                throw new IOException("The archive already contains an S:Startup-Sequence; it will not be replaced automatically.");
            // AmigaDOS 1.3 runs this script after the standard DOS boot code initializes dos.library.
            var script = Encoding.ASCII.GetBytes($"SYS:{relative}\n");
            blocks += (scriptDirectory is null ? 1 : 0) + FileBlocks(script.Length, ofs: true);
            CheckCapacity(blocks);

            await CreateImageAsync(output, volumeName, async (volume, token) =>
            {
                await WriteDirectoryAsync(volume, tree, "/", token);
                token.ThrowIfCancellationRequested();
                await volume.ChangeDirectory("/");
                if (scriptDirectory is null) await volume.CreateDirectory("S");
                await volume.ChangeDirectory("/S");
                await using var scriptSource = new MemoryStream(script, writable: false);
                await WriteFileAsync(volume, "Startup-Sequence", scriptSource, script.Length, token);
            }, cancellationToken, bootableA500: true);
        }, cancellationToken);

    private static string ValidateStartupPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.EndsWith('/') || normalized.Contains(':') ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".." ||
                segment.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-'))))
            throw new ArgumentException("The autostart program path must contain only ASCII letters, digits, dot, underscore or hyphen.", nameof(relativePath));
        return normalized;
    }

    private sealed record SourceFile(string Path, long Length);
    private sealed record SourceDirectory(string Name, List<SourceFile> Files, List<SourceDirectory> Children);

    private static SourceDirectory InspectDirectory(string path, ref long blocks, CancellationToken token, bool ofs = false)
    {
        token.ThrowIfCancellationRequested();
        PathSafety.RejectLinks(path);
        var result = new SourceDirectory(Path.GetFileName(path), [], []);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            PathSafety.RejectLinks(entry);
            var name = Path.GetFileName(entry);
            ValidateName(name);
            // DOS\1 folds ASCII letters; do not apply Unicode case folding to Latin-1 names.
            var key = new string(name.Select(c => c is >= 'a' and <= 'z' ? (char)(c - 32) : c).ToArray());
            if (!names.Add(key)) throw new IOException($"Amiga name collision: {entry}");
            if (Directory.Exists(entry))
            {
                CheckCapacity(++blocks);
                result.Children.Add(InspectDirectory(entry, ref blocks, token, ofs));
            }
            else
            {
                var length = new FileInfo(entry).Length;
                blocks += FileBlocks(length, ofs);
                CheckCapacity(blocks);
                result.Files.Add(new SourceFile(entry, length));
            }
        }
        return result;
    }

    private static long FileBlocks(long length, bool ofs = false)
    {
        // Lower bound for on-disk allocation. The library can need additional
        // working space at the capacity boundary; its DiskFullException is handled below.
        var payload = ofs ? BlockSize - 24 : BlockSize;
        var data = (length + payload - 1) / payload;
        return 1 + data + (data == 0 ? 0 : (data - 1) / 72);
    }

    private static void CheckCapacity(long blocks)
    {
        if (blocks > BlockCount)
            throw new IOException("The source does not fit on an 880 KiB ADF (including filesystem overhead).");
    }

    internal static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 30 || name is "." or ".." ||
            name.Any(c => c > 255 || char.IsControl(c) || c is '/' or '\\' or ':'))
            throw new ArgumentException("Amiga names must contain 1–30 Latin-1 characters without control characters, slash, backslash or colon.", nameof(name));
    }

    private static async Task CreateImageAsync(string outputPath, string volumeName,
        Func<FastFileSystemVolume, CancellationToken, Task> populate, CancellationToken token,
        bool bootableA500 = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ValidateName(volumeName);
        token.ThrowIfCancellationRequested();
        var output = Path.GetFullPath(outputPath);
        PathSafety.RejectLinks(output);
        var parent = Path.GetDirectoryName(output)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);

        // The fixed-size buffer also prevents a faulty writer from growing the image.
        var image = new byte[AdfSize];
        using (var stream = new MemoryStream(image, writable: true))
        {
            Trace.WriteLine("ADF: Formatting");
            await FastFileSystemFormatter.Format(stream, 0, 79, 2, 2, 11, 512, 512,
                new byte[] { (byte)'D', (byte)'O', (byte)'S', bootableA500 ? (byte)0 : (byte)1 }, volumeName);
            token.ThrowIfCancellationRequested();
            if (bootableA500)
            {
                var bootBlock = BootBlockBuilder.Build(new BootBlock
                {
                    DosType = [(byte)'D', (byte)'O', (byte)'S', 0],
                    RootBlockOffset = 880
                }, 1024);
                bootBlock.CopyTo(image, 0);
            }
            stream.Position = 0;
            var volume = await FastFileSystemVolume.MountAdf(stream);
            try
            {
                await populate(volume, token);
                token.ThrowIfCancellationRequested();
                Trace.WriteLine("ADF: Flushing volume");
                await volume.Flush();
                Trace.WriteLine("ADF: Volume flushed");
            }
            catch (Hst.Amiga.FileSystems.Exceptions.DiskFullException ex)
            {
                throw new IOException("The source does not fit on this ADF. The existing output has not been changed.", ex);
            }
            finally
            {
                Trace.WriteLine("ADF: Closing volume");
                volume.Dispose();
                Trace.WriteLine("ADF: Volume closed");
            }
        }
        token.ThrowIfCancellationRequested();
        var temporary = Path.Combine(parent, $".amidisklab-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var target = new FileStream(temporary, System.IO.FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await target.WriteAsync(image, token);
                await target.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            PathSafety.RejectLinks(output);
            File.Move(temporary, output, overwrite: true);
            Trace.WriteLine($"ADF: Published {output}");
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Trace.WriteLine($"ADF: Temporary file cleanup failed: {temporary}: {ex}"); }
        }
    }

    private static async Task WriteDirectoryAsync(FastFileSystemVolume volume, SourceDirectory directory,
        string amigaPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Trace.WriteLine($"ADF: Changing directory to {amigaPath}");
        await volume.ChangeDirectory(amigaPath);
        Trace.WriteLine($"ADF: Directory selected: {amigaPath}");
        foreach (var file in directory.Files)
        {
            token.ThrowIfCancellationRequested();
            PathSafety.RejectLinks(file.Path);
            await using var source = new FileStream(file.Path, System.IO.FileMode.Open,
                FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length != file.Length) throw new IOException($"Source changed: {file.Path}");
            await WriteFileAsync(volume, Path.GetFileName(file.Path), source, file.Length, token);
        }
        foreach (var child in directory.Children)
        {
            token.ThrowIfCancellationRequested();
            await volume.ChangeDirectory(amigaPath);
            Trace.WriteLine($"ADF: Creating directory {child.Name}");
            await volume.CreateDirectory(child.Name);
            Trace.WriteLine($"ADF: Directory created: {child.Name}");
            await WriteDirectoryAsync(volume, child, amigaPath.TrimEnd('/') + "/" + child.Name, token);
        }
    }

    private static async Task WriteFileAsync(FastFileSystemVolume volume, string name,
        Stream source, long length, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Trace.WriteLine($"ADF: Opening {name} ({length} bytes)");
        await using (var target = await volume.OpenFile(name, Hst.Amiga.FileSystems.FileMode.Write, overwrite: false))
        {
            var buffer = new byte[81920];
            long remaining = length;
            while (remaining > 0)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), token);
                if (read == 0) throw new IOException($"Source shortened: {name}");
                await target.WriteAsync(buffer.AsMemory(0, read), token);
                remaining -= read;
            }
            if (await source.ReadAsync(buffer.AsMemory(0, 1), token) != 0)
                throw new IOException($"Source grew: {name}");
            Trace.WriteLine($"ADF: Closing {name}");
        }
        Trace.WriteLine($"ADF: Finished {name}");
    }
}

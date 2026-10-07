using AmiDiskLab.Core.Services;
using AmiDiskLab.Core.Models;
using AmiDiskLab.Infrastructure.FileSystem;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AmiDiskLab.Infrastructure.Conversion;

public sealed class ArchiveToAdfConverter(IArchiveExtractor extractor, IAmigaDiskImageWriter writer)
    : IArchiveToAdfConverter
{
    private const long AdfSize = 901_120;

    public async Task<EmbeddedDiskExportResult> ExportEmbeddedDiskImagesAsync(string archivePath,
        string destinationFolder, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);
        var destinationFullPath = Path.GetFullPath(destinationFolder);
        PathSafety.RejectLinks(destinationFullPath);
        var temporary = Path.Combine(Path.GetTempPath(), $"AmiDiskLab-{Guid.NewGuid():N}");
        try
        {
            progress?.Report("Inspecting embedded disk images...");
            await extractor.ExtractAsync(archivePath, temporary, cancellationToken);
            var candidates = Directory.GetFiles(temporary, "*", SearchOption.AllDirectories)
                .Where(path => new FileInfo(path).Length == AdfSize && IsDiskImageName(Path.GetFileName(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
                throw new NotSupportedException("No complete 880 KiB disk image was found in this archive.");
            var archiveName = Path.GetFileNameWithoutExtension(archivePath);
            var names = candidates.Select((path, index) =>
                Path.Combine(destinationFullPath, $"{archiveName}_{index + 1:D2}_{Path.GetFileName(path)}.adf")).ToList();
            if (names.Any(path => File.Exists(path) || Directory.Exists(path)))
                throw new IOException("One or more output ADF files already exist.");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationFullPath);
            var created = new List<string>();
            try
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report($"Exporting embedded disk {i + 1} / {candidates.Count}...");
                    await using var input = new FileStream(candidates[i], FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using var output = new FileStream(names[i], FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    created.Add(names[i]);
                    await input.CopyToAsync(output, cancellationToken);
                }
            }
            catch
            {
                foreach (var path in created)
                    try { File.Delete(path); } catch (IOException) { }
                throw;
            }
            return new EmbeddedDiskExportResult(candidates.Count);
        }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Trace.WriteLine($"Temporary folder could not be removed: {temporary}: {ex}"); }
        }
    }

    private static bool IsDiskImageName(string name) =>
        Path.GetExtension(name).Equals(".adf", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(name, @"^disk[._ -]?\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<ConversionResult> ConvertAsync(string archivePath, string outputPath, string volumeName,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        await ConvertCoreAsync(archivePath, outputPath, volumeName, bootableA500: false, progress, cancellationToken);

    public async Task<ConversionResult> ConvertBootableA500Async(string archivePath, string outputPath, string volumeName,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default,
        AmigaTargetSystem? targetSystem = null) =>
        await ConvertCoreAsync(archivePath, outputPath, volumeName, bootableA500: true, progress, cancellationToken,
            targetSystem ?? AmigaTargetSystem.Default);

    private async Task<ConversionResult> ConvertCoreAsync(string archivePath, string outputPath, string volumeName,
        bool bootableA500, IProgress<string>? progress, CancellationToken cancellationToken,
        AmigaTargetSystem? targetSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        AmigaDiskImageWriter.ValidateName(volumeName);
        targetSystem?.Validate();
        if (Path.GetFullPath(archivePath).Equals(Path.GetFullPath(outputPath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("The output must not replace the source archive.");
        cancellationToken.ThrowIfCancellationRequested();
        var temporary = Path.Combine(Path.GetTempPath(), $"AmiDiskLab-{Guid.NewGuid():N}");
        string? cleanupWarning = null;
        try
        {
            progress?.Report("Extracting archive...");
            await extractor.ExtractAsync(archivePath, temporary, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (bootableA500)
            {
                progress?.Report("Checking A500 boot compatibility...");
                var files = Directory.GetFiles(temporary, "*", SearchOption.AllDirectories);
                if (files.Any(path => Path.GetExtension(path).Equals(".Slave", StringComparison.OrdinalIgnoreCase)))
                {
                    var target = targetSystem!;
                    var prerequisite = target.Kickstart == KickstartVersion.V13
                        ? " WHDLoad also requires Kickstart 2.0 or newer."
                        : string.Empty;
                    var embeddedDisks = files.Count(path => new FileInfo(path).Length == AdfSize &&
                        IsDiskImageName(Path.GetFileName(path)));
                    var nextStep = embeddedDisks > 0
                        ? $" This archive contains {embeddedDisks} embedded disk image(s); use Extract disk ADFs to export them unchanged, then check whether they boot."
                        : " No complete 880 KiB disk image was found; use original bootable disk images or a separately prepared WHDLoad environment.";
                    throw new NotSupportedException($"This is a WHDLoad package. Selected A500 target: Kickstart {target.KickstartLabel}, {target.RamMiB} MiB RAM. Automatic Gotek boot ADF creation from a WHDLoad package is not supported; its Slave cannot be launched by an AmigaDOS startup script.{prerequisite}{nextStep}");
                }
                var executables = new List<string>();
                foreach (var path in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (await AmigaExecutable.IsHunkFileAsync(path, cancellationToken))
                        executables.Add(Path.GetRelativePath(temporary, path));
                }
                if (executables.Count != 1)
                    throw new NotSupportedException(executables.Count == 0
                        ? "No AmigaDOS executable was found. A data archive or original game disk cannot be made auto-startable by adding a boot block."
                        : "Several AmigaDOS executables were found. Automatic A500 boot export needs exactly one start program.");
                progress?.Report("Creating A500 boot ADF...");
                await writer.CreateBootableA500AdfFromFolderAsync(temporary, outputPath, volumeName,
                    executables[0], cancellationToken);
            }
            else
            {
                progress?.Report("Creating data ADF...");
                await writer.CreateAdfFromFolderAsync(temporary, outputPath, volumeName, cancellationToken);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cleanupWarning = $"Temporary folder could not be removed: {temporary}";
                Trace.WriteLine($"{cleanupWarning}: {ex}");
            }
        }
        return new ConversionResult(cleanupWarning);
    }
}

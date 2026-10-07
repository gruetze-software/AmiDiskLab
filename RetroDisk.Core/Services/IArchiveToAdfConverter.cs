using AmiDiskLab.Core.Models;

namespace AmiDiskLab.Core.Services;

public sealed record ConversionResult(string? CleanupWarning = null);
public sealed record EmbeddedDiskExportResult(int DiskCount);

public interface IArchiveToAdfConverter
{
    Task<ConversionResult> ConvertAsync(string archivePath, string outputPath, string volumeName,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task<ConversionResult> ConvertBootableA500Async(string archivePath, string outputPath, string volumeName,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default,
        AmigaTargetSystem? targetSystem = null);

    Task<EmbeddedDiskExportResult> ExportEmbeddedDiskImagesAsync(string archivePath, string destinationFolder,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

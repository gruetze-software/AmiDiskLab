using System.Security.Cryptography;
using AmiDiskLab.Core.Models;
using AmiDiskLab.Core.Services;

namespace AmiDiskLab.Infrastructure.FileSystem;

public sealed class RetroSoftwareScanner : IRetroSoftwareScanner
{
    private static readonly Dictionary<string, SoftwareFormat> SupportedFormats =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".adf"] = SoftwareFormat.Adf,
                [".hfe"] = SoftwareFormat.Hfe,
                [".dms"] = SoftwareFormat.Dms,
                [".ipf"] = SoftwareFormat.Ipf,
                [".lha"] = SoftwareFormat.Lha
            };

    public Task<IReadOnlyList<RetroSoftwareItem>> ScanFolderAsync(string folderPath,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => ScanCoreAsync(folderPath, progress, cancellationToken), cancellationToken);
    private static async Task<IReadOnlyList<RetroSoftwareItem>> ScanCoreAsync(
            string folderPath,
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException(
                    "Folder path must not be empty.",
                    nameof(folderPath));

        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(
                    $"Folder '{folderPath}' does not exist.");

        progress?.Report(new ScanProgress
        {
            Status = "Searching for supported files..."
        });

        cancellationToken.ThrowIfCancellationRequested();
        PathSafety.RejectLinks(folderPath);
        var filePaths = Directory.EnumerateFiles(folderPath, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).Where(path =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SupportedFormats.ContainsKey(Path.GetExtension(path));
        });
        var items = new List<RetroSoftwareItem>();

        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(filePath);
            var format = SupportedFormats[Path.GetExtension(filePath)];

            items.Add(new RetroSoftwareItem
            {
                FileName = fileInfo.Name,
                FullPath = fileInfo.FullName,
                RelativePath = Path.GetRelativePath(
                            folderPath,
                            fileInfo.DirectoryName ?? folderPath),
                FileSize = fileInfo.Length,
                Format = format,
                Type = GetItemType(format)
            });
        }

        // Only files with identical sizes can possibly be duplicates.
        var candidates = items
                .GroupBy(item => item.FileSize)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .ToList();

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = candidates[i];

            progress?.Report(new ScanProgress
            {
                Current = i + 1,
                Total = candidates.Count,
                CurrentFile = item.FileName,
                Status = "Checking possible duplicates..."
            });

            item.Sha256 = await CalculateSha256Async(
                    item.FullPath,
                    cancellationToken);
        }

        var duplicateGroups = candidates
                .Where(item => !string.IsNullOrEmpty(item.Sha256))
                .GroupBy(item => item.Sha256)
                .Where(group => group.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            foreach (var item in group)
            {
                item.IsDuplicate = true;
            }
        }

        progress?.Report(new ScanProgress
        {
            Current = 1,
            Total = 1,
            Status = "Scan completed"
        });

        return items;
    }

    private static SoftwareItemType GetItemType(SoftwareFormat format)
    {
        return format switch
        {
            SoftwareFormat.Adf => SoftwareItemType.DiskImage,
            SoftwareFormat.Hfe => SoftwareItemType.DiskImage,
            SoftwareFormat.Dms => SoftwareItemType.DiskImage,
            SoftwareFormat.Ipf => SoftwareItemType.DiskImage,
            SoftwareFormat.Lha => SoftwareItemType.Archive,
            _ => SoftwareItemType.Unknown
        };
    }

    private static async Task<string> CalculateSha256Async(
            string filePath,
            CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);

        var hash = await SHA256.HashDataAsync(
                stream,
                cancellationToken);

        return Convert.ToHexString(hash);
    }
}

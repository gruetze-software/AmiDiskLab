namespace AmiDiskLab.Core.Services;

public interface IArchiveExtractor
{
	Task ExtractAsync(
			string archivePath,
			string destinationFolder,
			CancellationToken cancellationToken = default);
}
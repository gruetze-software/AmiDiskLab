namespace AmiDiskLab.Core.Services;

public interface IAmigaDiskImageWriter
{
	Task CreateBlankAdfAsync(
			string outputPath,
			string volumeName,
			CancellationToken cancellationToken = default);

	Task CreateAdfFromFolderAsync(
			string sourceFolder,
			string outputPath,
			string volumeName,
			CancellationToken cancellationToken = default);

	Task CreateAdfWithFileAsync(
			string outputPath,
			string volumeName,
			string fileName,
			byte[] fileData,
			CancellationToken cancellationToken = default);

    Task CreateBootableA500AdfFromFolderAsync(
            string sourceFolder,
            string outputPath,
            string volumeName,
            string executableRelativePath,
            CancellationToken cancellationToken = default);
}

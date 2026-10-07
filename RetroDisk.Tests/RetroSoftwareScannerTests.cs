using AmiDiskLab.Core.Models;
using AmiDiskLab.Infrastructure.FileSystem;
using System;
using System.Collections.Generic;
using System.Text;

namespace AmiDiskLab.Tests
{
	public class RetroSoftwareScannerTests
	{
		[Fact]
		public async Task ScanFolderAsync_FindsSupportedRetroSoftware()
		{
			// Arrange
			var tempFolder = Path.Combine(
					Path.GetTempPath(),
					Guid.NewGuid().ToString());

			Directory.CreateDirectory(tempFolder);

			try
			{
				await File.WriteAllTextAsync(
						Path.Combine(tempFolder, "Lotus2.adf"), "test");

				await File.WriteAllTextAsync(
						Path.Combine(tempFolder, "Turrican.HFE"), "test");

				await File.WriteAllTextAsync(
						Path.Combine(tempFolder, "AlienBreed.lha"), "test");

				await File.WriteAllTextAsync(
						Path.Combine(tempFolder, "Readme.txt"), "test");

				var scanner = new RetroSoftwareScanner();

				// Act
				var result = await scanner.ScanFolderAsync(tempFolder);

				// Assert
				Assert.Equal(3, result.Count);

				Assert.Contains(
						result,
						x => x.FileName == "Lotus2.adf"
								 && x.Format == SoftwareFormat.Adf
								 && x.Type == SoftwareItemType.DiskImage);

				Assert.Contains(
						result,
						x => x.FileName == "Turrican.HFE"
								 && x.Format == SoftwareFormat.Hfe
								 && x.Type == SoftwareItemType.DiskImage);

				Assert.Contains(
						result,
						x => x.FileName == "AlienBreed.lha"
								 && x.Format == SoftwareFormat.Lha
								 && x.Type == SoftwareItemType.Archive);

				Assert.DoesNotContain(
						result,
						x => x.FileName == "Readme.txt");
			}
			finally
			{
				Directory.Delete(tempFolder, true);
			}
		}
	}
}

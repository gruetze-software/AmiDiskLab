using AmiDiskLab.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace AmiDiskLab.Core.Services
{
	public interface IRetroSoftwareScanner
	{
		Task<IReadOnlyList<RetroSoftwareItem>> ScanFolderAsync(
				string folderPath,
				IProgress<ScanProgress>? progress = null,
				CancellationToken cancellationToken = default);
	}
}

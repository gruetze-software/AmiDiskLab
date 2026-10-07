using System;
using System.Collections.Generic;
using System.Text;

namespace AmiDiskLab.Core.Models
{
	public sealed class ScanProgress
	{
		public int Current { get; init; }

		public int Total { get; init; }

		public string? CurrentFile { get; init; }

		public string Status { get; init; } = string.Empty;

		public double Percentage =>
				Total == 0 ? 0 : (double)Current / Total * 100;
	}
}

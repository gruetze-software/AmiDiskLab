using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;

namespace AmiDiskLab.Core.Models
{
	public sealed class RetroSoftwareItem : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		public SoftwareMetadata Metadata { get; private set; } = new();
		public string? DiskSetTitle { get; init; }
		public IReadOnlyList<string>? DiskPaths { get; init; }
		public int DiskCount => DiskPaths?.Count ?? 1;
		public IReadOnlyList<string> AllPaths => DiskPaths ?? [FullPath];
		public string DisplayTitle => (string.IsNullOrWhiteSpace(Metadata.Title)
			? DiskSetTitle ?? FileName : Metadata.Title) +
			(DiskCount > 1 ? $" ({DiskCount} disks)" : string.Empty);
		public string DisplaySubtitle => DiskCount > 1
			? $"{DiskCount} disk images · {FileName}"
			: string.IsNullOrWhiteSpace(Metadata.Title) ? string.Empty : FileName;
		public string Studio => Metadata.Studio ?? string.Empty;
		public SoftwareCategory Category => Metadata.Category;
		public string Genre => Metadata.Genre ?? string.Empty;
		public string Publisher => Metadata.Publisher ?? string.Empty;
		public double? Rating => Metadata.Rating;
		public string RatingStars => Rating is not double rating ? string.Empty :
			new string('★', Math.Clamp((int)Math.Round(rating, MidpointRounding.AwayFromZero), 0, 5));
		public string ReleaseYear => Metadata.ReleaseDate is { Length: >= 4 } date &&
			int.TryParse(date[..4], out var year) && year is >= 1900 and <= 2099
			? date[..4] : string.Empty;
		public string DuplicateLabel => IsDuplicate ? "true" : string.Empty;

		public void SetMetadata(SoftwareMetadata metadata)
		{
			metadata.Validate();
			Metadata = metadata;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplaySubtitle)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Studio)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Category)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Genre)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Publisher)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rating)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RatingStars)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReleaseYear)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Metadata)));
		}
		public required string FileName { get; init; }

		public required string FullPath { get; init; }
		public required string RelativePath { get; init; }

		public string DisplaySize
		{
			get
			{
				if (FileSize < 1024)
					return $"{FileSize} B";

				if (FileSize < 1024 * 1024)
					return $"{FileSize / 1024.0:F1} KB";

				return $"{FileSize / 1024.0 / 1024.0:F1} MB";
			}
		}

		public long FileSize { get; init; }

		public SoftwareFormat Format { get; init; }

		public SoftwareItemType Type { get; init; }

		public string? Sha256 { get; set; }

		public bool IsDuplicate { get; set; }
	}
}

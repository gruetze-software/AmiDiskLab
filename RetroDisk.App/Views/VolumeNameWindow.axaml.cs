using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AmiDiskLab.App.Views;

public partial class VolumeNameWindow : Window
{
	public string? VolumeName { get; private set; }

	public VolumeNameWindow()
	{
		InitializeComponent();
	}

	public VolumeNameWindow(string suggestedVolumeName)
	{
		InitializeComponent();

		VolumeNameTextBox.Text = suggestedVolumeName;
	}

	private void Cancel_Click(
			object? sender,
			RoutedEventArgs e)
	{
		Close(false);
	}

	private void Create_Click(
			object? sender,
			RoutedEventArgs e)
	{
		var volumeName = VolumeNameTextBox.Text?.Trim();

		if (string.IsNullOrWhiteSpace(volumeName))
			return;

		VolumeName = volumeName;

		Close(true);
	}
}
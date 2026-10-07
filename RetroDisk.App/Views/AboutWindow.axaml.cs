using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using System.Reflection;

namespace AmiDiskLab.App.Views;

public partial class AboutWindow : Window
{
	public AboutWindow()
	{
		InitializeComponent();

		var version = Assembly
						.GetExecutingAssembly()
						.GetName()
						.Version;

		VersionText.Text = version is null
						? "Version"
						: $"Version {version.Major}.{version.Minor}";
	}

	private void Close_Click(
			object? sender,
			RoutedEventArgs e)
	{
		Close();
	}

	private void ExternalLink_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: string target } ||
			!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps ||
			uri.Host is not ("github.com" or "www.screenscraper.fr" or "demozoo.org" or "www.cloudflare.com"))
			return;
		try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); }
		catch (Exception ex) { Trace.WriteLine($"Could not open external link: {ex.Message}"); }
	}
}

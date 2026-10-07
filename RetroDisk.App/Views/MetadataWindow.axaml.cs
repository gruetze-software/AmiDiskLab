using AmiDiskLab.Core.Models;
using AmiDiskLab.Infrastructure.Metadata;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using System;
using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using System.Linq;

namespace AmiDiskLab.App.Views;

public partial class MetadataWindow : Window
{
    private static readonly HttpClient WikimediaHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly WikimediaPublisherLogoClient PublisherLogos = new(WikimediaHttp);
    private Bitmap? _cover;
    private Bitmap? _screenshot;
    private Bitmap? _publisherLogo;
    private Bitmap? _studioLogo;
    private string _publisherLogoSource = "offline cache";
    private string _metadataLanguage = "de";
    private string? _publisherForLogoRemoval;
    private string? _studioForLogoRemoval;
    private bool _closed;
    private string? _sourceUrl;
    public SoftwareMetadata Metadata { get; private set; } = new();

    public MetadataWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await LoadCoverAsync();
        Closed += (_, _) =>
        { _closed = true; _cover?.Dispose(); _screenshot?.Dispose(); _publisherLogo?.Dispose(); _studioLogo?.Dispose(); };
    }

    public MetadataWindow(RetroSoftwareItem item, MetadataSuggestion? suggestion, bool preferSuggestion = false,
        string? metadataLanguage = null) : this()
    {
        if (!string.IsNullOrWhiteSpace(metadataLanguage)) _metadataLanguage = metadataLanguage;
        _sourceUrl = Pick(item.Metadata.SourceUrl, suggestion?.Metadata.SourceUrl, preferSuggestion);
        FileNameText.Text = item.DiskCount > 1
            ? $"{item.DiskSetTitle} ({item.DiskCount} disks)" : item.FileName;
        TitleBox.Text = Pick(item.Metadata.Title, suggestion?.Metadata.Title, preferSuggestion);
        if (string.IsNullOrWhiteSpace(TitleBox.Text) && item.DiskSetTitle is not null)
            TitleBox.Text = item.DiskSetTitle;
        StudioBox.Text = Pick(item.Metadata.Studio, suggestion?.Metadata.Studio, preferSuggestion);
        StudioLogoStatus.Text = PublisherLogoStore.GetStudio(StudioBox.Text) is null
            ? "No studio logo available." : string.Empty;
        SceneGroupBox.Text = Pick(item.Metadata.SceneGroup, suggestion?.Metadata.SceneGroup, preferSuggestion);
        ProductionTypeBox.Text = Pick(item.Metadata.ProductionType, suggestion?.Metadata.ProductionType, preferSuggestion);
        PublisherBox.Text = Pick(item.Metadata.Publisher, suggestion?.Metadata.Publisher, preferSuggestion);
        PublisherLogoStore.ImportLegacy(item.Metadata.Publisher, item.Metadata.PublisherLogoUrl);
        var linkedLogo = PublisherLogoStore.Get(PublisherBox.Text);
        PublisherLogoUrlBox.Text = linkedLogo?.IsColor == true
            ? linkedLogo.Path
            : preferSuggestion
                ? suggestion?.Metadata.PublisherLogoUrl ?? linkedLogo?.Path ?? string.Empty
                : linkedLogo?.Path ?? item.Metadata.PublisherLogoUrl ?? suggestion?.Metadata.PublisherLogoUrl ?? string.Empty;
        PublisherLogoStatus.Text = string.IsNullOrWhiteSpace(PublisherLogoUrlBox.Text)
            ? "No publisher logo available." : string.Empty;
        ReleaseDateBox.Text = Pick(item.Metadata.ReleaseDate, suggestion?.Metadata.ReleaseDate, preferSuggestion);
        DescriptionBox.Text = Pick(item.Metadata.Description, suggestion?.Metadata.Description, preferSuggestion);
        GenreBox.Text = Pick(item.Metadata.Genre, suggestion?.Metadata.Genre, preferSuggestion);
        var rating = preferSuggestion ? suggestion?.Metadata.Rating ?? item.Metadata.Rating :
            item.Metadata.Rating ?? suggestion?.Metadata.Rating;
        RatingBox.Value = rating is null ? null : (decimal)rating.Value;
        CoverUrlBox.Text = Pick(item.Metadata.CoverUrl, suggestion?.Metadata.CoverUrl, preferSuggestion);
        ScreenshotUrlBox.Text = Pick(item.Metadata.ScreenshotUrl, suggestion?.Metadata.ScreenshotUrl, preferSuggestion);
        var sceneImageAsCover = (suggestion?.Metadata.SourceUrl?.Contains("demozoo.org", StringComparison.OrdinalIgnoreCase) == true ||
            item.Metadata.SourceUrl?.Contains("demozoo.org", StringComparison.OrdinalIgnoreCase) == true) &&
            string.IsNullOrWhiteSpace(CoverUrlBox.Text) && !string.IsNullOrWhiteSpace(ScreenshotUrlBox.Text);
        if (sceneImageAsCover) CoverUrlBox.Text = ScreenshotUrlBox.Text;
        if (sceneImageAsCover || suggestion?.Metadata.SourceUrl?.Contains("demozoo.org", StringComparison.OrdinalIgnoreCase) == true &&
            !string.IsNullOrWhiteSpace(CoverUrlBox.Text)) CoverLabel.Text = "Scene image (cover substitute)";
        CoverStatus.Text = string.IsNullOrWhiteSpace(CoverUrlBox.Text) ? "No cover available for this match." : "";
        ScreenshotStatus.Text = string.IsNullOrWhiteSpace(ScreenshotUrlBox.Text) ?
            "No screenshot available for this match." : "";
        CategoryBox.SelectedIndex = (int)(preferSuggestion && suggestion?.Metadata.Category != SoftwareCategory.Unknown
            ? suggestion!.Metadata.Category : item.Metadata.Category != SoftwareCategory.Unknown
            ? item.Metadata.Category : suggestion?.Metadata.Category ?? SoftwareCategory.Unknown);
        SourceText.Text = suggestion is null
            ? "Enter details manually. The original file will not be changed."
            : $"Suggested from {suggestion.Source}. Verify details before saving.";
    }

    private static string Pick(string? saved, string? suggested, bool preferSuggestion) =>
        (preferSuggestion ? suggested : saved) ?? saved ?? suggested ?? string.Empty;

    private async System.Threading.Tasks.Task LoadCoverAsync()
    {
        if (string.IsNullOrWhiteSpace(CoverUrlBox.Text) &&
            string.IsNullOrWhiteSpace(ScreenshotUrlBox.Text) &&
            string.IsNullOrWhiteSpace(PublisherLogoUrlBox.Text) &&
            string.IsNullOrWhiteSpace(PublisherBox.Text) &&
            string.IsNullOrWhiteSpace(StudioBox.Text)) return;
        Cursor = new Cursor(StandardCursorType.Wait);
        SaveButton.IsEnabled = false;
        try
        {
            await LoadPublisherLogoAsync();
            await LoadStudioLogoAsync();
            await LoadImageAsync(CoverUrlBox, CoverImage, CoverStatus, cover: true);
            await LoadImageAsync(ScreenshotUrlBox, ScreenshotImage, ScreenshotStatus, cover: false);
        }
        finally
        {
            if (!_closed) { Cursor = null; SaveButton.IsEnabled = true; }
        }
    }

    private async System.Threading.Tasks.Task LoadStudioLogoAsync()
    {
        var studio = Normalize(StudioBox.Text);
        if (studio is null) return;
        var linked = PublisherLogoStore.GetStudio(studio);
        var publisher = Normalize(PublisherBox.Text);
        if (linked is null && SameCompanyName(studio, publisher) &&
            PublisherLogoStore.Get(publisher) is { IsColor: true } publisherLogo)
        {
            Trace.WriteLine($"[StudioLogo] Reusing publisher logo for identical company '{studio}': " +
                publisherLogo.Path);
            PublisherLogoStore.SetStudio(studio, publisherLogo.Path, publisherLogo.IsColor);
            linked = PublisherLogoStore.GetStudio(studio);
        }
        string? path = linked?.Path;
        if (linked?.IsColor == true)
        {
            Trace.WriteLine($"[StudioLogo] Color cache hit for '{studio}': {linked.Path}");
        }
        else try
        {
            StudioLogoStatus.Text = "Downloading studio logo...";
            var releaseYear = ReleaseDateBox.Text is { Length: >= 4 } date ? date[..4] : null;
            Trace.WriteLine($"[StudioLogo] Wikimedia lookup for studio '{studio}'.");
            var url = await PublisherLogos.FindLogoAsync(studio, _metadataLanguage,
                TitleBox.Text, releaseYear);
            Trace.WriteLine($"[StudioLogo] Wikimedia result: {url ?? "none"}");
            path = await CoverCache.CacheAsync(url);
            if (path is not null) PublisherLogoStore.SetStudio(studio, path, true);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or
            System.Threading.Tasks.TaskCanceledException or ArgumentException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[StudioLogo] Lookup failed: {ex.GetType().Name}: {ex.Message}");
        }

        if (_closed) return;
        if (path is null)
        {
            StudioLogoStatus.Text = "No studio logo available.";
            return;
        }
        var bitmap = await CoverCache.LoadAsync(path);
        if (_closed) { bitmap?.Dispose(); return; }
        if (bitmap is null)
        {
            StudioLogoStatus.Text = "Studio logo could not be loaded.";
            return;
        }
        _studioLogo?.Dispose();
        _studioLogo = bitmap;
        StudioLogoImage.Source = bitmap;
        StudioLogoPanel.IsVisible = true;
        StudioLogoStatus.Text = "Studio logo available offline";
    }

    private async System.Threading.Tasks.Task LoadPublisherLogoAsync()
    {
        if (string.IsNullOrWhiteSpace(PublisherLogoUrlBox.Text) &&
            string.IsNullOrWhiteSpace(PublisherBox.Text)) return;
        PublisherLogoStatus.Text = "Downloading publisher logo...";
        try
        {
            var path = await CachePublisherLogoAsync(PublisherLogoUrlBox.Text, PublisherBox.Text);
            if (_closed) return;
            if (path is null) throw new IOException("Publisher logo unavailable.");
            PublisherLogoUrlBox.Text = path;
            var bitmap = await CoverCache.LoadAsync(path);
            if (_closed) { bitmap?.Dispose(); return; }
            if (bitmap is null) throw new IOException("Invalid publisher logo.");
            _publisherLogo = bitmap;
            PublisherLogoImage.Source = bitmap;
            PublisherLogoPanel.IsVisible = true;
            PublisherLogoStatus.Text = $"Publisher logo from {_publisherLogoSource} available offline";
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or ArgumentException or UnauthorizedAccessException)
        {
            if (_closed) return;
            Trace.WriteLine($"[PublisherLogo] Display failed: {ex.GetType().Name}: {ex.Message}");
            PublisherLogoUrlBox.Text = string.Empty;
            PublisherLogoStatus.Text = "Publisher logo could not be downloaded.";
        }
    }

    private async System.Threading.Tasks.Task<string?> CachePublisherLogoAsync(string? reference,
        string? publisher)
    {
        var isLocal = !string.IsNullOrWhiteSpace(reference) && Path.IsPathFullyQualified(reference);
        var isMonochrome = reference?.Contains("monochrome", StringComparison.OrdinalIgnoreCase) == true;
        var generatedColor = reference?.Contains("media=logo-couleur", StringComparison.OrdinalIgnoreCase) == true;
        var linked = PublisherLogoStore.Get(publisher);
        var knownMonochromeLocal = isLocal && linked is { IsColor: false } &&
            string.Equals(Path.GetFullPath(reference!), linked.Path, StringComparison.OrdinalIgnoreCase);
        Trace.WriteLine($"[PublisherLogo] Decision: publisher='{publisher ?? "(none)"}', " +
            $"reference={(isLocal ? "local" : isMonochrome ? "ScreenScraper monochrome" :
                generatedColor ? "ScreenScraper generated color" : string.IsNullOrWhiteSpace(reference) ? "none" : "remote")}." );
        if (!knownMonochromeLocal && (isLocal || !isMonochrome))
        {
            var existing = await CoverCache.CacheAsync(reference);
            if (existing is not null)
            {
                _publisherLogoSource = isLocal ? "offline cache" : "ScreenScraper";
                PublisherLogoStore.Set(publisher, existing, !isLocal || linked?.IsColor == true);
                return existing;
            }
        }

        try
        {
            var releaseYear = ReleaseDateBox.Text is { Length: >= 4 } date ? date[..4] : null;
            var wikimedia = await PublisherLogos.FindLogoAsync(publisher, _metadataLanguage,
                TitleBox.Text, releaseYear);
            Trace.WriteLine($"[PublisherLogo] Wikimedia result: {wikimedia ?? "none"}");
            var alternative = await CoverCache.CacheAsync(wikimedia);
            if (alternative is not null)
            {
                _publisherLogoSource = "Wikimedia Commons";
                PublisherLogoStore.Set(publisher, alternative, true);
                return alternative;
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or
            System.Threading.Tasks.TaskCanceledException)
        { Trace.WriteLine($"[PublisherLogo] Wikimedia lookup failed: {ex.GetType().Name}: {ex.Message}"); }

        if (isMonochrome)
        {
            Trace.WriteLine("[PublisherLogo] Falling back to ScreenScraper monochrome logo.");
            _publisherLogoSource = "ScreenScraper (monochrome)";
            var monochromePath = await CoverCache.CacheAsync(reference);
            if (monochromePath is not null) PublisherLogoStore.Set(publisher, monochromePath, false);
            return monochromePath;
        }
        if (!generatedColor) return knownMonochromeLocal ? linked?.Path : null;
        var monochrome = reference!.Replace("media=logo-couleur", "media=logo-monochrome",
            StringComparison.OrdinalIgnoreCase);
        _publisherLogoSource = "ScreenScraper (monochrome)";
        var fallback = await CoverCache.CacheAsync(monochrome);
        if (fallback is not null) PublisherLogoStore.Set(publisher, fallback, false);
        return fallback;
    }

    private async System.Threading.Tasks.Task LoadImageAsync(TextBox box, Image image, TextBlock status, bool cover)
    {
        if (string.IsNullOrWhiteSpace(box.Text)) return;
        status.Text = cover ? "Downloading cover..." : "Downloading screenshot...";
        try
        {
            var path = await CoverCache.CacheAsync(box.Text);
            if (_closed) return;
            if (path is null) throw new IOException("Image unavailable.");
            box.Text = path;
            var bitmap = await CoverCache.LoadAsync(path);
            if (_closed) { bitmap?.Dispose(); return; }
            if (bitmap is null) throw new IOException("Invalid image.");
            if (cover) _cover = bitmap;
            else _screenshot = bitmap;
            image.Source = bitmap;
            image.IsVisible = true;
            status.Text = cover ? "Cover available offline" : "Screenshot available offline";
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or ArgumentException or UnauthorizedAccessException)
        {
            if (_closed) return;
            Trace.WriteLine($"[MediaCache] Dialog image load failed: {ex.GetType().Name}: {ex.Message}");
            box.Text = string.Empty;
            status.Text = cover ? "Cover could not be downloaded." : "Screenshot could not be downloaded.";
        }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (CategoryBox.SelectedIndex < 0) return;
        var metadata = new SoftwareMetadata(Normalize(TitleBox.Text), Normalize(StudioBox.Text),
            (SoftwareCategory)CategoryBox.SelectedIndex, Normalize(ReleaseDateBox.Text),
            Normalize(PublisherBox.Text), Normalize(DescriptionBox.Text), Normalize(CoverUrlBox.Text),
            Normalize(ScreenshotUrlBox.Text), Normalize(SceneGroupBox.Text),
            Normalize(ProductionTypeBox.Text), Normalize(_sourceUrl), Normalize(GenreBox.Text),
            null, RatingBox.Value is decimal rating ? (double)rating : null);
        metadata.Validate();
        try
        {
            if (metadata.CoverUrl is not null)
            {
                var local = await CoverCache.CacheAsync(metadata.CoverUrl);
                if (local is null) { CoverStatus.Text = "Cover download failed; check the URL or connection."; return; }
                metadata = metadata with { CoverUrl = local };
            }
            if (metadata.ScreenshotUrl is not null)
            {
                var local = await CoverCache.CacheAsync(metadata.ScreenshotUrl);
                if (local is null)
                { ScreenshotStatus.Text = "Screenshot download failed; check the URL or connection."; return; }
                metadata = metadata with { ScreenshotUrl = local };
            }
            if (metadata.PublisherLogoUrl is not null)
            {
                var local = await CachePublisherLogoAsync(metadata.PublisherLogoUrl, metadata.Publisher);
                if (local is null)
                { PublisherLogoStatus.Text = "Publisher logo download failed; check the connection."; return; }
                metadata = metadata with { PublisherLogoUrl = local };
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or ArgumentException)
        { ScreenshotStatus.Text = "Media download failed; check the connection."; return; }
        Metadata = metadata;
        if (_publisherForLogoRemoval is not null)
            PublisherLogoStore.Remove(_publisherForLogoRemoval);
        if (_studioForLogoRemoval is not null)
            PublisherLogoStore.RemoveStudio(_studioForLogoRemoval);
        Close(true);
    }

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool SameCompanyName(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(right)) return false;
        static string Key(string value) =>
            new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return Key(left) == Key(right);
    }

    private void CoverImage_DoubleTapped(object? sender, RoutedEventArgs e) =>
        OpenImage(CoverUrlBox.Text, CoverStatus);

    private void ScreenshotImage_DoubleTapped(object? sender, RoutedEventArgs e) =>
        OpenImage(ScreenshotUrlBox.Text, ScreenshotStatus);

    private void PublisherLogoImage_DoubleTapped(object? sender, RoutedEventArgs e) =>
        OpenImage(PublisherLogoUrlBox.Text, PublisherLogoStatus);

    private void StudioLogoImage_DoubleTapped(object? sender, RoutedEventArgs e) =>
        OpenImage(PublisherLogoStore.GetStudio(StudioBox.Text)?.Path, StudioLogoStatus);

    private void RemovePublisherLogo_Click(object? sender, RoutedEventArgs e)
    {
        _publisherForLogoRemoval = Normalize(PublisherBox.Text);
        PublisherLogoUrlBox.Text = string.Empty;
        PublisherLogoImage.Source = null;
        PublisherLogoPanel.IsVisible = false;
        PublisherLogoStatus.Text = "Publisher logo will be removed when you save.";
    }

    private void RemoveStudioLogo_Click(object? sender, RoutedEventArgs e)
    {
        _studioForLogoRemoval = Normalize(StudioBox.Text);
        StudioLogoImage.Source = null;
        StudioLogoPanel.IsVisible = false;
        StudioLogoStatus.Text = "Studio logo will be removed when you save.";
    }

    private static void OpenImage(string? path, TextBlock status)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { status.Text = "Could not open the image in the default app."; }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}

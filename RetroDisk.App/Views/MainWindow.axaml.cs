using AmiDiskLab.App.ViewModels;
using AmiDiskLab.Core.Models;
using AmiDiskLab.Infrastructure.Archives;
using AmiDiskLab.Infrastructure.FileSystem;
using AmiDiskLab.Infrastructure.Metadata;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AmiDiskLab.App.Views;

public partial class MainWindow : Window
{
    private bool _dialogOpen;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SoftwareContextMenu.DataContext = DataContext;
        Opened += async (_, _) =>
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            var lastFolder = new LastFolderStore().Load();
            if (lastFolder is null) return;
            if (!Directory.Exists(lastFolder))
            {
                viewModel.StatusText = "The last folder is no longer available.";
                return;
            }
            await viewModel.LoadFolderAsync(lastFolder);
        };
        Closing += (_, args) =>
        {
            if (DataContext is MainWindowViewModel { IsBusy: true } viewModel)
            {
                args.Cancel = true;
                viewModel.Cancel();
            }
        };
    }

    private async void SelectFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogOpen || DataContext is not MainWindowViewModel { IsBusy: false } viewModel) return;
        _dialogOpen = true;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select folder containing disk images",
                AllowMultiple = false
            });
            if (folders.Count == 0) return;
            var path = folders[0].TryGetLocalPath() ?? throw new IOException("Please select a local folder.");
            await viewModel.LoadFolderAsync(path);
            if (string.Equals(viewModel.SelectedFolder, path, StringComparison.OrdinalIgnoreCase))
                new LastFolderStore().Save(path);
        }
        catch (Exception ex) { viewModel.ReportError("Folder selection failed", ex); }
        finally { _dialogOpen = false; }
    }

    private async void About_Click(object? sender, RoutedEventArgs e)
    {
        try { await new AboutWindow().ShowDialog(this); }
        catch (Exception ex)
        {
            if (DataContext is MainWindowViewModel viewModel) viewModel.ReportError("About dialog failed", ex);
        }
    }

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogOpen || DataContext is not MainWindowViewModel { IsBusy: false } viewModel) return;
        _dialogOpen = true;
        try
        {
            var dialog = new ConfigurationWindow(viewModel.TargetSystem, viewModel.ScreenScraperAccess,
                viewModel.ScreenScraperPreferences, UiPreferencesStore.Load());
            if (!await dialog.ShowDialog<bool>(this)) return;
            new ScreenScraperAccessStore().Save(dialog.Access);
            TargetSystemSettingsStore.Save(dialog.TargetSystem);
            viewModel.TargetSystem = dialog.TargetSystem;
            viewModel.ScreenScraperAccess = dialog.Access;
            ScreenScraperPreferencesStore.Save(dialog.Preferences);
            viewModel.ScreenScraperPreferences = dialog.Preferences;
            UiPreferencesStore.Save(dialog.UiPreferences);
            ThemeManager.Apply(dialog.UiPreferences.Theme);
        }
        catch (Exception ex) { viewModel.ReportError("Target settings failed", ex); }
        finally { _dialogOpen = false; }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel) viewModel.Cancel();
    }

    private async void CreateAdf_Click(object? sender, RoutedEventArgs e) =>
        await CreateAdfFromSelectionAsync(bootableA500: false);

    private async void CreateBootAdf_Click(object? sender, RoutedEventArgs e) =>
        await CreateAdfFromSelectionAsync(bootableA500: true);

    private async void ExtractDiskAdfs_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogOpen || DataContext is not MainWindowViewModel viewModel || !viewModel.CanCreateAdf) return;
        var archive = viewModel.SelectedItem!.FullPath;
        _dialogOpen = true;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose folder for embedded disk ADFs",
                AllowMultiple = false
            });
            if (folders.Count == 0) return;
            var path = folders[0].TryGetLocalPath() ?? throw new IOException("Please select a local output folder.");
            await viewModel.ExportEmbeddedDiskImagesAsync(archive, path);
        }
        catch (Exception ex) { viewModel.ReportError("Disk export failed", ex); }
        finally { _dialogOpen = false; }
    }

    private async void EditMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogOpen || DataContext is not MainWindowViewModel viewModel || !viewModel.CanEditMetadata) return;
        var item = viewModel.SelectedItem!;
        _dialogOpen = true;
        try
        {
            MetadataSuggestion? suggestion = null;
            if (item.Format == SoftwareFormat.Lha)
            {
                try
                {
                    var readme = await ReadmeMetadataSuggester.SuggestAsync(item.FullPath);
                    if (readme is not null) suggestion = new MetadataSuggestion(readme, "archive ReadMe");
                }
                catch (Exception ex)
                { viewModel.ReportError("ReadMe metadata could not be read", ex); }
            }
            else if (item.Format == SoftwareFormat.Adf)
            {
                try { suggestion = await AdfMetadataSuggester.SuggestAsync(item.FullPath); }
                catch (Exception ex)
                { viewModel.ReportError("ADF metadata could not be read", ex); }
            }
            var dialog = new MetadataWindow(item, suggestion,
                metadataLanguage: viewModel.ScreenScraperPreferences.Language);
            if (await dialog.ShowDialog<bool>(this)) viewModel.SaveMetadata(item, dialog.Metadata);
        }
        catch (Exception ex) { viewModel.ReportError("Metadata editing failed", ex); }
        finally { _dialogOpen = false; }
    }

    private void SoftwareList_DoubleTapped(object? sender, RoutedEventArgs e) =>
        EditMetadata_Click(sender, e);

    private void SoftwareList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed ||
            DataContext is not MainWindowViewModel viewModel || e.Source is not Visual source) return;
        var row = source as ListBoxItem ?? source.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        viewModel.SelectedItem = row?.DataContext as RetroSoftwareItem;
    }

    private async void ShowCover_Click(object? sender, RoutedEventArgs e) =>
        await ShowMediaAsync(screenshot: false);

    private async void ShowScreenshot_Click(object? sender, RoutedEventArgs e) =>
        await ShowMediaAsync(screenshot: true);

    private async System.Threading.Tasks.Task ShowMediaAsync(bool screenshot)
    {
        if (_dialogOpen || DataContext is not MainWindowViewModel { IsBusy: false } viewModel ||
            viewModel.SelectedItem is null) return;
        var reference = screenshot ? viewModel.SelectedItem.Metadata.ScreenshotUrl :
            viewModel.SelectedItem.Metadata.CoverUrl;
        if (string.IsNullOrWhiteSpace(reference)) return;
        _dialogOpen = true;
        try
        {
            var localPath = await CoverCache.CacheAsync(reference);
            if (localPath is null) throw new IOException("The image is not available locally.");
            Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { viewModel.ReportError(screenshot ? "Opening screenshot failed" : "Opening cover failed", ex); }
        finally { _dialogOpen = false; }
    }

    private void OpenPath_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogOpen || DataContext is not MainWindowViewModel { IsBusy: false } viewModel ||
            viewModel.SelectedItem is null) return;
        try
        {
            var item = viewModel.SelectedItem;
            var folder = Path.GetDirectoryName(item.FullPath);
            if (folder is null || !Directory.Exists(folder))
                throw new DirectoryNotFoundException("The file's folder is no longer available.");
            if (item.AllPaths.Count == 1 && OperatingSystem.IsWindows())
            {
                if (!File.Exists(item.FullPath))
                    throw new FileNotFoundException("The selected file is no longer available.", item.FullPath);
                var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                startInfo.ArgumentList.Add("/select,");
                startInfo.ArgumentList.Add(item.FullPath);
                Process.Start(startInfo);
            }
            else Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { viewModel.ReportError("Opening folder failed", ex); }
    }

    private void SortHeader_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string column } && DataContext is MainWindowViewModel viewModel)
            viewModel.SortByColumn(column);
    }

    private async void ScreenScraper_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogOpen || DataContext is not MainWindowViewModel viewModel || !viewModel.CanLookupOnline) return;
        var item = viewModel.SelectedItem!;
        _dialogOpen = true;
        try
        {
            if (viewModel.ScreenScraperAccess is null)
            {
                if (!await ConfigureScreenScraperAsync(viewModel)) return;
            }
            var matches = await viewModel.LookupOnlineMetadataAsync(item.FullPath);
            if (matches.Count == 0) return;
            MetadataSuggestion? suggestion;
            if (matches.Count == 1) suggestion = matches[0];
            else
            {
                var picker = new ScreenScraperMatchesWindow(matches);
                if (!await picker.ShowDialog<bool>(this)) return;
                suggestion = picker.SelectedMatch;
            }
            if (suggestion is null) return;
            var dialog = new MetadataWindow(item, suggestion, preferSuggestion: true,
                metadataLanguage: viewModel.ScreenScraperPreferences.Language);
            if (await dialog.ShowDialog<bool>(this)) viewModel.SaveMetadata(item, dialog.Metadata);
        }
        catch (Exception ex) { viewModel.ReportError("ScreenScraper metadata failed", ex); }
        finally { _dialogOpen = false; }
    }

    private async void BatchMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogOpen || DataContext is not MainWindowViewModel { IsBusy: false } viewModel) return;
        _dialogOpen = true;
        try
        {
            if (viewModel.ScreenScraperAccess is null && !await ConfigureScreenScraperAsync(viewModel)) return;
            await new BatchMetadataWindow(viewModel).ShowDialog(this);
        }
        catch (Exception ex) { viewModel.ReportError("Batch metadata lookup failed", ex); }
        finally { _dialogOpen = false; }
    }

    private async void Demozoo_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogOpen || DataContext is not MainWindowViewModel viewModel || !viewModel.CanLookupScene) return;
        var item = viewModel.SelectedItem!;
        _dialogOpen = true;
        try
        {
            var suggestedTitle = DemozooClient.SearchTitle(item.FileName, item.Metadata.Title, item.DiskSetTitle);
            var searchDialog = new SceneSearchWindow(suggestedTitle);
            if (!await searchDialog.ShowDialog<bool>(this)) return;
            var matches = await viewModel.LookupSceneMetadataAsync(searchDialog.Query);
            if (matches.Count == 0) return;
            MetadataSuggestion? suggestion;
            if (matches.Count == 1) suggestion = matches[0];
            else
            {
                var picker = new ScreenScraperMatchesWindow(matches, "Demozoo matches",
                    "Title matches are suggestions; the disk image was not verified. Check the group and production type.");
                if (!await picker.ShowDialog<bool>(this)) return;
                suggestion = picker.SelectedMatch;
            }
            if (suggestion is null) return;
            var dialog = new MetadataWindow(item, suggestion, preferSuggestion: true,
                metadataLanguage: viewModel.ScreenScraperPreferences.Language);
            if (await dialog.ShowDialog<bool>(this)) viewModel.SaveMetadata(item, dialog.Metadata);
        }
        catch (Exception ex) { viewModel.ReportError("Demozoo metadata failed", ex); }
        finally { _dialogOpen = false; }
    }

    private async System.Threading.Tasks.Task<bool> ConfigureScreenScraperAsync(MainWindowViewModel viewModel)
    {
        var dialog = new ConfigurationWindow(viewModel.TargetSystem, viewModel.ScreenScraperAccess,
            viewModel.ScreenScraperPreferences, UiPreferencesStore.Load(),
            showScreenScraper: true);
        if (!await dialog.ShowDialog<bool>(this)) return false;
        new ScreenScraperAccessStore().Save(dialog.Access);
        TargetSystemSettingsStore.Save(dialog.TargetSystem);
        viewModel.TargetSystem = dialog.TargetSystem;
        viewModel.ScreenScraperAccess = dialog.Access;
        ScreenScraperPreferencesStore.Save(dialog.Preferences);
        viewModel.ScreenScraperPreferences = dialog.Preferences;
        return dialog.Access is not null;
    }

    private async System.Threading.Tasks.Task CreateAdfFromSelectionAsync(bool bootableA500)
    {
        if (_dialogOpen || DataContext is not MainWindowViewModel viewModel || !viewModel.CanCreateAdf) return;
        var selectedItem = viewModel.SelectedItem!;
        _dialogOpen = true;
        try
        {
            var suggested = Path.GetFileNameWithoutExtension(selectedItem.FileName);
            var dialog = new VolumeNameWindow(suggested.Length > 30 ? suggested[..30] : suggested);
            if (!await dialog.ShowDialog<bool>(this) || string.IsNullOrWhiteSpace(dialog.VolumeName)) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Create Amiga ADF",
                SuggestedFileName = $"{dialog.VolumeName}.adf",
                DefaultExtension = "adf",
                FileTypeChoices = [new FilePickerFileType("Amiga Disk File") { Patterns = ["*.adf"] }]
            });
            if (file is null) return;
            var path = file.TryGetLocalPath() ?? throw new IOException("Please select a local output file.");
            await viewModel.CreateAdfAsync(selectedItem.FullPath, path, dialog.VolumeName, bootableA500);
        }
        catch (Exception ex) { viewModel.ReportError("ADF creation failed", ex); }
        finally { _dialogOpen = false; }
    }
}

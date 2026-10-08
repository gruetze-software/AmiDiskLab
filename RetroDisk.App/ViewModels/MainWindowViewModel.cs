using CommunityToolkit.Mvvm.ComponentModel;
using AmiDiskLab.Core.Models;
using AmiDiskLab.Core.Services;
using AmiDiskLab.Infrastructure.Metadata;
using System.Net.Http;
using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AmiDiskLab.App.ViewModels;

public partial class MainWindowViewModel(IRetroSoftwareScanner scanner, IArchiveToAdfConverter converter,
    SoftwareMetadataStore? metadataStore = null) : ViewModelBase
{
    private readonly SoftwareMetadataStore _metadataStore = metadataStore ?? new();
    private readonly ScreenScraperClient _screenScraper = new(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
    private readonly DemozooClient _demozoo = new(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
    public ScreenScraperAccess? ScreenScraperAccess { get; set; } =
        new ScreenScraperAccessStore().Load() ?? new("", "");
    public ScreenScraperPreferences ScreenScraperPreferences { get; set; } = ScreenScraperPreferencesStore.Load();
    private CancellationTokenSource? _operation;
    private readonly object _progressLock = new();
    public ObservableCollection<RetroSoftwareItem> SoftwareItems { get; } = [];
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    public string NameSortHeader => SortHeader("Name", "Name");
    public string TypeSortHeader => SortHeader("Type", "Type");
    public string FormatSortHeader => SortHeader("Format", "Format");
    public string CategorySortHeader => SortHeader("Category", "Category");
    public string GenreSortHeader => SortHeader("Genre", "Genre");
    public string PublisherSortHeader => SortHeader("Publisher", "Publisher");
    public string StudioSortHeader => SortHeader("Studio", "Studio");
    public string YearSortHeader => SortHeader("Year", "Year");
    public string RatingSortHeader => SortHeader("Rating", "Rating");
    public string SizeSortHeader => SortHeader("Size", "Size");

    private string SortHeader(string column, string label) =>
        label + (column == _sortColumn ? (_sortAscending ? "  ↑" : "  ↓") : "");

    public void SortByColumn(string column)
    {
        if (IsBusy || column is not ("Name" or "Type" or "Format" or "Category" or "Genre" or
            "Publisher" or "Studio" or "Year" or "Rating" or "Size")) return;
        if (_sortColumn == column) _sortAscending = !_sortAscending;
        else { _sortColumn = column; _sortAscending = true; }
        ApplySort();
        NotifySortHeaders();
    }

    private void ApplySort()
    {
        var source = SoftwareItems.AsEnumerable();
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<RetroSoftwareItem> ordered = _sortColumn switch
        {
            "Type" => _sortAscending ? source.OrderBy(item => item.Type.ToString(), comparer) :
                source.OrderByDescending(item => item.Type.ToString(), comparer),
            "Format" => _sortAscending ? source.OrderBy(item => item.Format.ToString(), comparer) :
                source.OrderByDescending(item => item.Format.ToString(), comparer),
            "Category" => _sortAscending ? source.OrderBy(item => item.Category.ToString(), comparer) :
                source.OrderByDescending(item => item.Category.ToString(), comparer),
            "Genre" => _sortAscending ? source.OrderBy(item => item.Genre, comparer) :
                source.OrderByDescending(item => item.Genre, comparer),
            "Publisher" => _sortAscending ? source.OrderBy(item => item.Publisher, comparer) :
                source.OrderByDescending(item => item.Publisher, comparer),
            "Studio" => _sortAscending ? source.OrderBy(item => item.Studio, comparer) :
                source.OrderByDescending(item => item.Studio, comparer),
            "Year" => _sortAscending ? source.OrderBy(item => item.ReleaseYear, comparer) :
                source.OrderByDescending(item => item.ReleaseYear, comparer),
            "Rating" => _sortAscending ? source.OrderBy(item => item.Rating ?? -1) :
                source.OrderByDescending(item => item.Rating ?? -1),
            "Size" => _sortAscending ? source.OrderBy(item => item.FileSize) :
                source.OrderByDescending(item => item.FileSize),
            _ => _sortAscending ? source.OrderBy(item => item.DisplayTitle, comparer) :
                source.OrderByDescending(item => item.DisplayTitle, comparer)
        };
        var sorted = ordered.ThenBy(item => item.DisplayTitle, comparer).ToList();
        var selection = SelectedItem;
        SoftwareItems.Clear();
        foreach (var item in sorted) SoftwareItems.Add(item);
        SelectedItem = selection;
    }

    private void NotifySortHeaders()
    {
        OnPropertyChanged(nameof(NameSortHeader));
        OnPropertyChanged(nameof(TypeSortHeader));
        OnPropertyChanged(nameof(FormatSortHeader));
        OnPropertyChanged(nameof(CategorySortHeader));
        OnPropertyChanged(nameof(GenreSortHeader));
        OnPropertyChanged(nameof(PublisherSortHeader));
        OnPropertyChanged(nameof(StudioSortHeader));
        OnPropertyChanged(nameof(YearSortHeader));
        OnPropertyChanged(nameof(RatingSortHeader));
        OnPropertyChanged(nameof(SizeSortHeader));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetSystemSummary))]
    private AmigaTargetSystem _targetSystem = TargetSystemSettingsStore.Load();

    public string TargetSystemSummary => $"A500 · KS {TargetSystem.KickstartLabel} · {TargetSystem.RamMiB} MiB";
    public string ProductSubtitle
    {
        get
        {
            var version = typeof(MainWindowViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                .Split('+')[0];
            return $"Amiga Disk & Software Manager Version {version ?? "unknown"}";
        }
    }

    [ObservableProperty] private string _selectedFolder = "No folder selected";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private double _scanProgress;
    [ObservableProperty] private string _currentFile = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateAdf))]
    [NotifyPropertyChangedFor(nameof(CanEditMetadata))]
    [NotifyPropertyChangedFor(nameof(CanLookupOnline))]
    [NotifyPropertyChangedFor(nameof(CanLookupScene))]
    [NotifyPropertyChangedFor(nameof(CanShowCover))]
    [NotifyPropertyChangedFor(nameof(CanShowScreenshot))]
    [NotifyPropertyChangedFor(nameof(CanOpenPath))]
    [NotifyPropertyChangedFor(nameof(CanStartOperation))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateAdf))]
    [NotifyPropertyChangedFor(nameof(CanEditMetadata))]
    [NotifyPropertyChangedFor(nameof(CanLookupOnline))]
    [NotifyPropertyChangedFor(nameof(CanLookupScene))]
    [NotifyPropertyChangedFor(nameof(CanShowCover))]
    [NotifyPropertyChangedFor(nameof(CanShowScreenshot))]
    [NotifyPropertyChangedFor(nameof(CanOpenPath))]
    private RetroSoftwareItem? _selectedItem;

    public bool CanStartOperation => !IsBusy;
    public bool CanCreateAdf => !IsBusy && SelectedItem?.Format == SoftwareFormat.Lha;
    public bool CanEditMetadata => !IsBusy && SelectedItem is not null;
    public bool CanLookupOnline => !IsBusy && SelectedItem?.Format == SoftwareFormat.Adf;
    public bool CanLookupScene => !IsBusy && SelectedItem is not null;

    public async Task<IReadOnlyList<MetadataSuggestion>> LookupSceneMetadataAsync(string query)
    {
        if (IsBusy) return [];
        using var operation = BeginOperation($"Searching Demozoo for {query}...");
        try
        {
            var matches = await _demozoo.SearchAsync(query, operation.Token);
            StatusText = matches.Count == 0 ? $"No Amiga scene matches for '{query}'. Check the title or production ID." :
                $"{matches.Count} Demozoo candidate(s) found. Verify the production before saving.";
            return matches;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        { StatusText = "Demozoo lookup cancelled."; return []; }
        catch (Exception ex) { ReportError("Demozoo lookup failed", ex); return []; }
        finally { EndOperation(); }
    }
    public bool CanShowCover => !IsBusy && !string.IsNullOrWhiteSpace(SelectedItem?.Metadata.CoverUrl);
    public bool CanShowScreenshot => !IsBusy && !string.IsNullOrWhiteSpace(SelectedItem?.Metadata.ScreenshotUrl);
    public bool CanOpenPath => !IsBusy && SelectedItem is not null;

    public async Task<IReadOnlyList<MetadataSuggestion>> LookupOnlineMetadataAsync(string adfPath)
    {
        if (IsBusy || ScreenScraperAccess is null) return [];
        using var operation = BeginOperation("Searching ScreenScraper...");
        try
        {
            var matches = await _screenScraper.SearchAdfAsync(adfPath, ScreenScraperAccess,
                ScreenScraperPreferences, operation.Token);
            StatusText = matches.Count == 0 ? "No ScreenScraper candidates found." :
                $"{matches.Count} ScreenScraper candidate(s) found. Choose and verify the details.";
            return matches;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        { StatusText = "ScreenScraper lookup cancelled."; return []; }
        catch (Exception ex) { ReportError("ScreenScraper lookup failed", ex); return []; }
        finally { EndOperation(); }
    }

    public void SaveSelectedMetadata(SoftwareMetadata metadata)
    {
        if (!CanEditMetadata) return;
        SaveMetadata(SelectedItem!, metadata);
    }

    public void SaveMetadata(RetroSoftwareItem item, SoftwareMetadata metadata)
    {
        if (IsBusy || !SoftwareItems.Contains(item)) return;
        _metadataStore.SetMany(item.AllPaths, metadata);
        item.SetMetadata(metadata);
        ApplySort();
        OnPropertyChanged(nameof(CanShowCover));
        OnPropertyChanged(nameof(CanShowScreenshot));
        StatusText = $"Metadata saved: {item.FileName}";
    }

    public void SaveBatchMetadata(RetroSoftwareItem item, SoftwareMetadata metadata)
    {
        _metadataStore.SetMany(item.AllPaths, metadata);
        item.SetMetadata(metadata);
    }

    public void FinishBatchMetadata(string status)
    {
        ApplySort();
        OnPropertyChanged(nameof(CanShowCover));
        OnPropertyChanged(nameof(CanShowScreenshot));
        StatusText = status;
    }

    public void Cancel()
    {
        if (_operation is null) return;
        StatusText = "Cancelling...";
        _operation.Cancel();
    }

    public async Task LoadFolderAsync(string folderPath)
    {
        if (IsBusy) return;
        using var operation = BeginOperation("Starting scan...");
        var progress = new Progress<ScanProgress>(p =>
        {
            lock (_progressLock)
            {
                if (_operation != operation || operation.IsCancellationRequested) return;
                ScanProgress = p.Percentage;
                StatusText = p.Status;
                CurrentFile = string.IsNullOrWhiteSpace(p.CurrentFile) ? string.Empty :
                    $"{p.Current} / {p.Total} - {p.CurrentFile}";
            }
        });
        try
        {
            var items = await scanner.ScanFolderAsync(folderPath, progress, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            StopProgress();
            SelectedItem = null;
            SoftwareItems.Clear();
            var groupedItems = DiskSetGrouper.Group(items);
            foreach (var item in groupedItems)
            {
                var saved = item.AllPaths.Select(_metadataStore.Get)
                    .FirstOrDefault(metadata => metadata != new SoftwareMetadata()) ?? new SoftwareMetadata();
                item.SetMetadata(saved);
                SoftwareItems.Add(item);
            }
            _sortColumn = "Name";
            _sortAscending = true;
            ApplySort();
            NotifySortHeaders();
            SelectedFolder = folderPath;
            StatusText = $"{groupedItems.Count} game(s)/item(s), {items.Count} file(s) | " +
                $"{groupedItems.Count(x => x.IsDuplicate)} duplicate(s)";
            ScanProgress = 100;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        { StopProgress(); StatusText = "Scan cancelled."; }
        catch (Exception ex) { StopProgress(); ReportError("Scan failed", ex); }
        finally { EndOperation(); }
    }

    public Task CreateAdfAsync(string archivePath, string outputPath, string volumeName) =>
        CreateAdfAsync(archivePath, outputPath, volumeName, bootableA500: false);

    public async Task ExportEmbeddedDiskImagesAsync(string archivePath, string destinationFolder)
    {
        if (IsBusy) return;
        using var operation = BeginOperation("Inspecting archive...");
        try
        {
            var result = await converter.ExportEmbeddedDiskImagesAsync(archivePath, destinationFolder,
                new Progress<string>(message =>
                {
                    lock (_progressLock)
                    {
                        if (_operation == operation && !operation.IsCancellationRequested) StatusText = message;
                    }
                }), operation.Token);
            StatusText = $"Exported {result.DiskCount} embedded disk image(s). Boot compatibility has not been verified.";
            ScanProgress = 100;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        { StatusText = "Disk export cancelled."; }
        catch (Exception ex) { ReportError("Disk export failed", ex); }
        finally { EndOperation(); }
    }

    public async Task CreateAdfAsync(string archivePath, string outputPath, string volumeName, bool bootableA500)
    {
        if (IsBusy) return;
        using var operation = BeginOperation("Preparing ADF...");
        var progress = new Progress<string>(message =>
        {
            lock (_progressLock)
            {
                if (_operation == operation && !operation.IsCancellationRequested) StatusText = message;
            }
        });
        try
        {
            var result = bootableA500
                ? await converter.ConvertBootableA500Async(archivePath, outputPath, volumeName, progress, operation.Token, TargetSystem)
                : await converter.ConvertAsync(archivePath, outputPath, volumeName, progress, operation.Token);
            StopProgress();
            StatusText = $"{(bootableA500 ? "A500 boot ADF" : "Data ADF")} created: {volumeName}" +
                (result.CleanupWarning is null ? string.Empty : $" | {result.CleanupWarning}");
            ScanProgress = 100;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        { StopProgress(); StatusText = "ADF creation cancelled."; }
        catch (Exception ex) { StopProgress(); ReportError("ADF creation failed", ex); }
        finally { EndOperation(); }
    }

    public void ReportError(string context, Exception exception)
    {
        StatusText = $"{context}: {exception.Message}";
        Trace.WriteLine(exception);
    }

    private CancellationTokenSource BeginOperation(string status)
    {
        var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        ScanProgress = 0;
        CurrentFile = string.Empty;
        StatusText = status;
        return operation;
    }

    private void EndOperation()
    {
        StopProgress();
        IsBusy = false;
        CurrentFile = string.Empty;
    }

    private void StopProgress()
    {
        lock (_progressLock) _operation = null;
    }
}

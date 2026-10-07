using AmiDiskLab.App.ViewModels;
using AmiDiskLab.App;
using AmiDiskLab.Core.Models;
using AmiDiskLab.Core.Services;
using AmiDiskLab.Infrastructure.Conversion;

namespace AmiDiskLab.Tests;

public class WorkflowTests
{
    [Fact]
    public async Task FailedScanKeepsPreviousResultsAndRestoresActions()
    {
        var scanner = new ScannerStub();
        var vm = new MainWindowViewModel(scanner, new ConverterStub());
        await vm.LoadFolderAsync("first");
        scanner.Fail = true;
        await vm.LoadFolderAsync("second");
        Assert.Equal("first", vm.SelectedFolder);
        Assert.Single(vm.SoftwareItems);
        Assert.StartsWith("Scan failed:", vm.StatusText);
        Assert.False(vm.IsBusy);
        Assert.True(vm.CanStartOperation);
    }

    [Fact]
    public async Task ScanLoadsSavedMetadataAndEditsPersist()
    {
        using var folder = new TestFolder();
        var metadataPath = folder.File("settings/metadata.json");
        var store = new SoftwareMetadataStore(metadataPath);
        store.Set("test.lha", new SoftwareMetadata("First title", "First studio", SoftwareCategory.Game));
        var vm = new MainWindowViewModel(new ScannerStub(), new ConverterStub(), store);
        await vm.LoadFolderAsync("folder");
        var item = Assert.Single(vm.SoftwareItems);
        Assert.Equal("First title", item.DisplayTitle);
        Assert.Equal("First studio", item.Studio);
        vm.SelectedItem = item;
        Assert.True(vm.CanOpenPath);
        Assert.False(vm.CanShowCover);
        Assert.False(vm.CanShowScreenshot);
        vm.SaveSelectedMetadata(new SoftwareMetadata("New title", "New studio", SoftwareCategory.Demo,
            CoverUrl: "cover.png", ScreenshotUrl: "screenshot.jpg"));
        Assert.Equal("New title", item.DisplayTitle);
        Assert.Equal(SoftwareCategory.Demo, item.Category);
        Assert.True(vm.CanShowCover);
        Assert.True(vm.CanShowScreenshot);
        Assert.Equal("New title", new SoftwareMetadataStore(metadataPath).Get("test.lha").Title);
    }

    [Fact]
    public async Task DiskSetMetadataIsStoredForEveryDisk()
    {
        using var folder = new TestFolder();
        var store = new SoftwareMetadataStore(folder.File("metadata.json"));
        var scanner = new ScannerStub
        {
            Items = [new RetroSoftwareItem
            {
                FileName = "Ambermoon (A).adf", FullPath = folder.File("Ambermoon (A).adf"),
                RelativePath = ".", Format = SoftwareFormat.Adf, Type = SoftwareItemType.DiskImage
            }, new RetroSoftwareItem
            {
                FileName = "Ambermoon (B).adf", FullPath = folder.File("Ambermoon (B).adf"),
                RelativePath = ".", Format = SoftwareFormat.Adf, Type = SoftwareItemType.DiskImage
            }]
        };
        var vm = new MainWindowViewModel(scanner, new ConverterStub(), store);
        await vm.LoadFolderAsync("folder");
        var game = Assert.Single(vm.SoftwareItems);
        Assert.Equal(2, game.DiskCount);
        vm.SaveMetadata(game, new SoftwareMetadata("Ambermoon", Category: SoftwareCategory.Game));
        Assert.Equal("Ambermoon", store.Get(scanner.Items[0].FullPath).Title);
        Assert.Equal("Ambermoon", store.Get(scanner.Items[1].FullPath).Title);
    }

    [Fact]
    public async Task ScanSortsNamesAndColumnSortTogglesDirection()
    {
        using var folder = new TestFolder();
        var scanner = new ScannerStub
        {
            Items = [TestAdf("Zulu.adf", 3), TestAdf("Alpha.adf", 2), TestAdf("Beta.adf", 1)]
        };
        var vm = new MainWindowViewModel(scanner, new ConverterStub(),
            new SoftwareMetadataStore(folder.File("metadata.json")));
        await vm.LoadFolderAsync("folder");
        Assert.Equal(["Alpha.adf", "Beta.adf", "Zulu.adf"],
            vm.SoftwareItems.Select(item => item.FileName));
        Assert.Contains("↑", vm.NameSortHeader);

        vm.SelectedItem = vm.SoftwareItems[1];
        vm.SortByColumn("Size");
        Assert.Equal(["Beta.adf", "Alpha.adf", "Zulu.adf"],
            vm.SoftwareItems.Select(item => item.FileName));
        Assert.Equal("Beta.adf", vm.SelectedItem?.FileName);
        vm.SortByColumn("Size");
        Assert.Equal(["Zulu.adf", "Alpha.adf", "Beta.adf"],
            vm.SoftwareItems.Select(item => item.FileName));
        Assert.Contains("↓", vm.SizeSortHeader);

        vm.SortByColumn("Name");
        vm.SaveMetadata(vm.SoftwareItems[2], new SoftwareMetadata("Aardvark"));
        Assert.Equal("Aardvark", vm.SoftwareItems[0].DisplayTitle);
    }

    [Fact]
    public void LastFolderStoreRemembersChosenFolder()
    {
        using var folder = new TestFolder();
        var settingsPath = folder.File("last-folder.json");
        var selected = Path.GetDirectoryName(settingsPath)!;
        var store = new LastFolderStore(settingsPath);
        store.Save(selected);
        Assert.Equal(selected, new LastFolderStore(settingsPath).Load());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(0.0, "")]
    [InlineData(1.4, "★")]
    [InlineData(3.6, "★★★★")]
    [InlineData(5.0, "★★★★★")]
    public void RatingDisplaysOnlyFullStars(double? rating, string expected)
    {
        var item = TestAdf("Rating.adf", 901120);
        item.SetMetadata(new SoftwareMetadata(Rating: rating));
        Assert.Equal(expected, item.RatingStars);
        Assert.DoesNotContain('☆', item.RatingStars);
    }

    private static RetroSoftwareItem TestAdf(string name, long size) => new()
    {
        FileName = name, FullPath = name, RelativePath = ".", FileSize = size,
        Format = SoftwareFormat.Adf, Type = SoftwareItemType.DiskImage
    };

    [Fact]
    public async Task BusyConversionRejectsAnotherOperationAndCanBeCancelled()
    {
        var scanner = new ScannerStub();
        var converter = new ConverterStub { WaitForCancellation = true };
        var vm = new MainWindowViewModel(scanner, converter);
        var running = vm.CreateAdfAsync("archive", "output", "TEST");
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanCreateAdf);
        await vm.LoadFolderAsync("ignored");
        await vm.CreateAdfAsync("ignored", "ignored", "TEST");
        Assert.Equal(0, scanner.Calls);
        Assert.Equal(1, converter.Calls);
        vm.Cancel();
        await running;
        Assert.False(vm.IsBusy);
        Assert.Equal("ADF creation cancelled.", vm.StatusText);
    }

    [Fact]
    public async Task FailedConversionRestoresActions()
    {
        var vm = new MainWindowViewModel(new ScannerStub(), new ConverterStub { Fail = true });
        await vm.CreateAdfAsync("archive", "output", "TEST");
        Assert.False(vm.IsBusy);
        Assert.StartsWith("ADF creation failed:", vm.StatusText);
    }

    [Fact]
    public async Task LateProgressCannotOverwriteCompletedStatus()
    {
        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            var vm = new MainWindowViewModel(new ScannerStub(), new ConverterStub());
            var task = vm.LoadFolderAsync("folder");
            Assert.True(task.IsCompleted);
            await task;
            var status = vm.StatusText;
            context.Drain();
            Assert.Equal(status, vm.StatusText);
            Assert.Equal(100, vm.ScanProgress);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public async Task ConverterCleansExtractionFolderWhenWriterFails()
    {
        var extractor = new ExtractorStub();
        var writer = new WriterStub { Fail = true };
        var converter = new ArchiveToAdfConverter(extractor, writer);
        await Assert.ThrowsAsync<IOException>(() => converter.ConvertAsync("source.lha", "target.adf", "TEST"));
        Assert.NotNull(extractor.Destination);
        Assert.False(Directory.Exists(extractor.Destination));
        Assert.Equal(1, writer.Calls);
    }

    [Fact]
    public async Task ConverterCleansOnCancellationBetweenStages()
    {
        using var cancellation = new CancellationTokenSource();
        var extractor = new ExtractorStub { AfterExtract = cancellation.Cancel };
        var writer = new WriterStub();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ArchiveToAdfConverter(extractor, writer).ConvertAsync("source.lha", "target.adf", "TEST", cancellationToken: cancellation.Token));
        Assert.Equal(0, writer.Calls);
        Assert.False(Directory.Exists(extractor.Destination));
    }

    [Fact]
    public async Task ConverterRejectsOverwritingSourceArchive()
    {
        var extractor = new ExtractorStub();
        await Assert.ThrowsAsync<IOException>(() =>
            new ArchiveToAdfConverter(extractor, new WriterStub()).ConvertAsync("same.lha", "same.lha", "TEST"));
        Assert.Null(extractor.Destination);
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue((callback, state));
        public void Drain() { while (_queue.TryDequeue(out var item)) item.Callback(item.State); }
    }

    private sealed class ScannerStub : IRetroSoftwareScanner
    {
        public bool Fail { get; set; }
        public IReadOnlyList<RetroSoftwareItem>? Items { get; set; }
        public int Calls { get; private set; }
        public Task<IReadOnlyList<RetroSoftwareItem>> ScanFolderAsync(string path, IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail) throw new IOException("Unreadable folder");
            progress?.Report(new ScanProgress { Status = "Old progress" });
            return Task.FromResult<IReadOnlyList<RetroSoftwareItem>>(Items ?? [new RetroSoftwareItem
            { FileName = "test.lha", FullPath = "test.lha", RelativePath = ".", Format = SoftwareFormat.Lha }]);
        }
    }

    private sealed class ConverterStub : IArchiveToAdfConverter
    {
        public Task<EmbeddedDiskExportResult> ExportEmbeddedDiskImagesAsync(string archivePath,
            string destinationFolder, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmbeddedDiskExportResult(1));
        public bool WaitForCancellation { get; set; }
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public async Task<ConversionResult> ConvertAsync(string archivePath, string outputPath, string volumeName,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail) throw new IOException("Write failed");
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ConversionResult();
        }

        public Task<ConversionResult> ConvertBootableA500Async(string archivePath, string outputPath,
            string volumeName, IProgress<string>? progress = null, CancellationToken cancellationToken = default,
            AmiDiskLab.Core.Models.AmigaTargetSystem? targetSystem = null) =>
            ConvertAsync(archivePath, outputPath, volumeName, progress, cancellationToken);
    }

    private sealed class ExtractorStub : IArchiveExtractor
    {
        public string? Destination { get; private set; }
        public Action? AfterExtract { get; set; }
        public Task ExtractAsync(string archivePath, string destinationFolder, CancellationToken cancellationToken = default)
        {
            Destination = destinationFolder;
            Directory.CreateDirectory(destinationFolder);
            File.WriteAllText(Path.Combine(destinationFolder, "test"), "data");
            AfterExtract?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class WriterStub : IAmigaDiskImageWriter
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Task CreateAdfFromFolderAsync(string sourceFolder, string outputPath, string volumeName,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail) throw new IOException("Write failed");
            return Task.CompletedTask;
        }
        public Task CreateBlankAdfAsync(string outputPath, string volumeName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateAdfWithFileAsync(string outputPath, string volumeName, string fileName, byte[] data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateBootableA500AdfFromFolderAsync(string sourceFolder, string outputPath,
            string volumeName, string executableRelativePath, CancellationToken cancellationToken = default) =>
            CreateAdfFromFolderAsync(sourceFolder, outputPath, volumeName, cancellationToken);
    }
}

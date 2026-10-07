using AmiDiskLab.Infrastructure.Archives;
using AmiDiskLab.Infrastructure.FileSystem;

namespace AmiDiskLab.Tests;

public class InputSafetyTests
{
    [Fact]
    public void NonEmptyDirectoriesDifferingOnlyByCaseCannotMerge()
    {
        using var folder = new TestFolder();
        Assert.Throws<InvalidDataException>(() => LhaArchiveExtractor.ValidatePathCasing(folder.Path,
            [folder.File("Data/first"), folder.File("data/second")]));
    }

    [Fact]
    public async Task CorruptArchiveCannotReplaceExistingImage()
    {
        using var folder = new TestFolder();
        var archive = folder.File("broken.lha");
        var output = folder.File("existing.adf");
        await File.WriteAllBytesAsync(archive, [10, 20, 30, 40, 50, 60, 70, 80]);
        await File.WriteAllBytesAsync(output, [42]);
        var converter = new AmiDiskLab.Infrastructure.Conversion.ArchiveToAdfConverter(
            new LhaArchiveExtractor(), new AmigaDiskImageWriter());
        var error = await Record.ExceptionAsync(() => converter.ConvertAsync(archive, output, "TEST"));
        Assert.NotNull(error);
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task ArchiveConversionServiceProducesReadableContents()
    {
        using var folder = new TestFolder();
        var archive = await SyntheticLhaArchive.CreateAsync(folder);
        var converter = new AmiDiskLab.Infrastructure.Conversion.ArchiveToAdfConverter(
            new LhaArchiveExtractor(), new AmigaDiskImageWriter());
        var result = await converter.ConvertAsync(archive, folder.File("test.adf"), "TEST");
        Assert.Null(result.CleanupWarning);
        await new LhaArchiveExtractor().ExtractAsync(archive, folder.File("expected"));
        await AmigaDiskImageWriterTests.AssertTree(folder.File("expected"), folder.File("test.adf"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/../../escape")]
    [InlineData("/absolute")]
    [InlineData("C:\\escape")]
    [InlineData("\\\\server\\share")]
    [InlineData("a:stream")]
    [InlineData("../escape\0ignored")]
    [InlineData("\0hidden")]
    [InlineData("CON")]
    [InlineData("a/../b")]
    [InlineData("a./b")]
    [InlineData("a /b")]
    [InlineData("a//b")]
    public void InvalidArchivePathsAreRejected(string path)
    {
        using var folder = new TestFolder();
        Assert.Throws<InvalidDataException>(() => LhaArchiveExtractor.GetSafeTargetPath(folder.Path, path));
    }

    [Fact]
    public void NullTerminatedLegacyPathsAndSeparatorsAreSupported()
    {
        using var folder = new TestFolder();
        Assert.Equal(folder.File(Path.Combine("dir", "file")), LhaArchiveExtractor.GetSafeTargetPath(folder.Path, "dir\\file\0"));
        Assert.Equal(folder.File(Path.Combine("dir", "file")), LhaArchiveExtractor.GetSafeTargetPath(folder.Path, "dir\\file\0extra data"));
    }

    [Fact]
    public async Task ExtractionNeverOverwritesExistingFile()
    {
        using var folder = new TestFolder();
        var archive = await SyntheticLhaArchive.CreateAsync(folder);
        await File.WriteAllBytesAsync(folder.File("Sample.info"), [42]);
        await Assert.ThrowsAsync<IOException>(() => new LhaArchiveExtractor().ExtractAsync(
            archive, folder.Path));
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(folder.File("Sample.info")));
    }

    [Fact]
    public async Task CancelledExtractionDoesNotCreateDestination()
    {
        using var folder = new TestFolder();
        var archive = await SyntheticLhaArchive.CreateAsync(folder);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LhaArchiveExtractor().ExtractAsync(
            archive, folder.File("output"), new CancellationToken(true)));
        Assert.False(Directory.Exists(folder.File("output")));
    }

    [Fact]
    public async Task ScannerFindsDuplicatesAcrossFoldersByContent()
    {
        using var folder = new TestFolder();
        Directory.CreateDirectory(folder.File("child"));
        await File.WriteAllBytesAsync(folder.File("first.adf"), [1, 2]);
        await File.WriteAllBytesAsync(folder.File("child/second.ADF"), [1, 2]);
        await File.WriteAllBytesAsync(folder.File("different.ipf"), [2, 1]);
        await File.WriteAllBytesAsync(folder.File("unique.dms"), [3]);
        var items = await new RetroSoftwareScanner().ScanFolderAsync(folder.Path);
        Assert.Equal(4, items.Count);
        Assert.Equal(2, items.Count(item => item.IsDuplicate));
        Assert.False(items.Single(item => item.FileName == "different.ipf").IsDuplicate);
        Assert.Equal("child", items.Single(item => item.FileName == "second.ADF").RelativePath);
    }

    [Fact]
    public async Task CancelledScanOfEmptyFolderIsCancelled()
    {
        using var folder = new TestFolder();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RetroSoftwareScanner().ScanFolderAsync(folder.Path, cancellationToken: new CancellationToken(true)));
    }
}

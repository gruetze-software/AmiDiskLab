using Directory = System.IO.Directory;
using File = System.IO.File;
using AmiDiskLab.Infrastructure.Archives;
using AmiDiskLab.Infrastructure.FileSystem;
using Hst.Amiga.FileSystems.FastFileSystem;

namespace AmiDiskLab.Tests;

public sealed class TestFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AmiDiskLab-Test-{Guid.NewGuid():N}");
    public TestFolder() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}

public class AmigaDiskImageWriterTests
{
    [Fact]
    public async Task BlankImageCanBeReopened()
    {
        using var folder = new TestFolder();
        var output = folder.File("blank.adf");
        await new AmigaDiskImageWriter().CreateBlankAdfAsync(output, "TESTDISK");
        Assert.Equal(901120, new FileInfo(output).Length);
        var image = await File.ReadAllBytesAsync(output);
        Assert.Equal(new byte[] { 68, 79, 83, 1 }, image[..4]);
        using var stream = File.OpenRead(output);
        using var volume = await FastFileSystemVolume.MountAdf(stream);
        Assert.Empty(await volume.ListEntries());
        const int root = 880 * 512;
        Assert.Equal("TESTDISK", System.Text.Encoding.Latin1.GetString(image, root + 433, image[root + 432]));
        Assert.Equal(0u, BlockSum(image, root));
        var bitmap = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(root + 316)) * 512;
        Assert.Equal(0u, BlockSum(image, bitmap));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(36864)]
    [InlineData(36865)]
    [InlineData(400000)]
    public async Task FileContentsSurviveReopen(int size)
    {
        using var folder = new TestFolder();
        var data = Enumerable.Range(0, size).Select(i => (byte)(i * 17 + 3)).ToArray();
        await new AmigaDiskImageWriter().CreateAdfWithFileAsync(folder.File("test.adf"), "TEST", "PAYLOAD", data);
        using var stream = File.OpenRead(folder.File("test.adf"));
        using var volume = await FastFileSystemVolume.MountAdf(stream);
        await AssertFile(volume, "PAYLOAD", data);
    }

    [Fact]
    public async Task FolderPreservesSiblingsNestedAndEmptyDirectories()
    {
        using var folder = new TestFolder();
        var source = folder.File("source");
        Directory.CreateDirectory(System.IO.Path.Combine(source, "A", "Child"));
        Directory.CreateDirectory(System.IO.Path.Combine(source, "B"));
        Directory.CreateDirectory(System.IO.Path.Combine(source, "Empty"));
        await File.WriteAllBytesAsync(System.IO.Path.Combine(source, "root"), [1, 2, 3]);
        await File.WriteAllBytesAsync(System.IO.Path.Combine(source, "A", "Child", "file"), [4, 5]);
        await File.WriteAllBytesAsync(System.IO.Path.Combine(source, "B", "file"), [6, 7, 8]);
        await new AmigaDiskImageWriter().CreateAdfFromFolderAsync(source, folder.File("test.adf"), "TREE");
        await AssertTree(source, folder.File("test.adf"));
    }

    [Fact]
    public async Task CapacityIncludesFileExtensionBlocks()
    {
        using var folder = new TestFolder();
        var output = folder.File("full.adf");
        var data = new byte[1730 * 512];
        data[^1] = 123;
        var writer = new AmigaDiskImageWriter();
        await writer.CreateAdfWithFileAsync(output, "FULL", "PAYLOAD", data);
        using (var stream = File.OpenRead(output))
        using (var volume = await FastFileSystemVolume.MountAdf(stream))
            await AssertFile(volume, "PAYLOAD", data);
        var before = await File.ReadAllBytesAsync(output);
        await Assert.ThrowsAsync<IOException>(() => writer.CreateAdfWithFileAsync(output, "FULL", "PAYLOAD", new byte[1731 * 512 + 1]));
        Assert.Equal(before, await File.ReadAllBytesAsync(output));
        // Hst.Amiga 0.6.238 also needs working space at exact full capacity.
        var failure = await Assert.ThrowsAsync<IOException>(() => writer.CreateAdfWithFileAsync(output, "FULL", "PAYLOAD", new byte[1731 * 512]));
        Assert.IsType<Hst.Amiga.FileSystems.Exceptions.DiskFullException>(failure.InnerException);
        Assert.Equal(before, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task CancellationAfterFileClosePreservesOutput()
    {
        using var folder = new TestFolder();
        var output = folder.File("test.adf");
        await File.WriteAllBytesAsync(output, [42]);
        using var cancellation = new CancellationTokenSource();
        var name = "CANCEL" + Guid.NewGuid().ToString("N")[..16];
        using var listener = new CancelOnMessage("ADF: Finished " + name, cancellation);
        System.Diagnostics.Trace.Listeners.Add(listener);
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AmigaDiskImageWriter()
                .CreateAdfWithFileAsync(output, "TEST", name, new byte[1025], cancellation.Token));
        }
        finally { System.Diagnostics.Trace.Listeners.Remove(listener); }
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.GetFiles(folder.Path, ".amidisklab-*.tmp"));
    }

    private sealed class CancelOnMessage(string expected, CancellationTokenSource cancellation) : System.Diagnostics.TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { if (message == expected) cancellation.Cancel(); }
    }

    [Fact]
    public async Task SyntheticArchiveRoundTripsThroughProductionWriter()
    {
        using var folder = new TestFolder();
        var source = folder.File("source");
        var archive = await SyntheticLhaArchive.CreateAsync(folder);
        await new LhaArchiveExtractor().ExtractAsync(archive, source);
        Assert.Equal(2, Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length);
        await new AmigaDiskImageWriter().CreateAdfFromFolderAsync(source, folder.File("sample.adf"), "SAMPLE");
        await AssertTree(source, folder.File("sample.adf"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567890123456789012345678901")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    [InlineData("emoji😀")]
    [InlineData("a\0b")]
    public async Task InvalidNamesPreserveExistingOutput(string name)
    {
        using var folder = new TestFolder();
        var output = folder.File("test.adf");
        await File.WriteAllBytesAsync(output, [42]);
        var writer = new AmigaDiskImageWriter();
        await Assert.ThrowsAsync<ArgumentException>(() => writer.CreateBlankAdfAsync(output, name));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.CreateAdfWithFileAsync(output, "TEST", name, [1]));
        var source = folder.File("source");
        Directory.CreateDirectory(source);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.CreateAdfFromFolderAsync(source, output, name));
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task OversizeAndCancellationPreserveExistingOutput()
    {
        using var folder = new TestFolder();
        var output = folder.File("test.adf");
        await File.WriteAllBytesAsync(output, [42]);
        var writer = new AmigaDiskImageWriter();
        await Assert.ThrowsAsync<IOException>(() => writer.CreateAdfWithFileAsync(output, "TEST", "BIG", new byte[901120]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.CreateBlankAdfAsync(output, "TEST", new CancellationToken(true)));
        var source = folder.File("source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(System.IO.Path.Combine(source, "BIG"), new byte[901120]);
        await Assert.ThrowsAsync<IOException>(() => writer.CreateAdfFromFolderAsync(source, output, "TEST"));
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.GetFiles(folder.Path, ".amidisklab-*.tmp"));
    }

    [Fact]
    public async Task OutputInsideSourceIsRejected()
    {
        using var folder = new TestFolder();
        var output = folder.File("test.adf");
        await File.WriteAllBytesAsync(output, [42]);
        await Assert.ThrowsAsync<IOException>(() => new AmigaDiskImageWriter().CreateAdfFromFolderAsync(folder.Path, output, "TEST"));
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task PublishingFailureCleansTemporaryOutput()
    {
        using var folder = new TestFolder();
        var output = folder.File("existing-directory");
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(System.IO.Path.Combine(output, "sentinel"), [42]);
        var failure = await Record.ExceptionAsync(() => new AmigaDiskImageWriter().CreateBlankAdfAsync(output, "TEST"));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(System.IO.Path.Combine(output, "sentinel")));
        Assert.Empty(Directory.GetFiles(folder.Path, ".amidisklab-*.tmp"));
    }

    [Fact]
    public async Task SuccessfulWriteReplacesExistingOutput()
    {
        using var folder = new TestFolder();
        var output = folder.File("test.adf");
        await File.WriteAllBytesAsync(output, [42]);
        await new AmigaDiskImageWriter().CreateBlankAdfAsync(output, "TEST");
        Assert.Equal(901120, new FileInfo(output).Length);
        Assert.Empty(Directory.GetFiles(folder.Path, ".amidisklab-*.tmp"));
    }

    internal static async Task AssertTree(string source, string image)
    {
        using var stream = File.OpenRead(image);
        using var volume = await FastFileSystemVolume.MountAdf(stream);
        foreach (var directory in new[] { source }.Concat(Directory.GetDirectories(source, "*", SearchOption.AllDirectories)))
        {
            var relative = System.IO.Path.GetRelativePath(source, directory).Replace('\\', '/');
            await volume.ChangeDirectory(relative == "." ? "/" : "/" + relative);
            Assert.Equal(Directory.GetFileSystemEntries(directory).Select(System.IO.Path.GetFileName).Order(),
                (await volume.ListEntries()).Select(entry => entry.Name).Order());
            foreach (var file in Directory.GetFiles(directory))
                await AssertFile(volume, System.IO.Path.GetFileName(file), await File.ReadAllBytesAsync(file));
        }
    }

    private static async Task AssertFile(FastFileSystemVolume volume, string name, byte[] expected)
    {
        await using var file = await volume.OpenFile(name, Hst.Amiga.FileSystems.FileMode.Read);
        using var actual = new MemoryStream();
        await file.CopyToAsync(actual);
        Assert.Equal(expected, actual.ToArray());
    }

    private static uint BlockSum(byte[] image, int offset)
    {
        uint sum = 0;
        for (var i = 0; i < 512; i += 4)
            sum = unchecked(sum + System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(offset + i)));
        return sum;
    }
}

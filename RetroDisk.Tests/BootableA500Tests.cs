using Directory = System.IO.Directory;
using File = System.IO.File;
using AmiDiskLab.Infrastructure.Archives;
using AmiDiskLab.Infrastructure.Conversion;
using AmiDiskLab.Infrastructure.FileSystem;
using Amiga.FileFormats.LHA;
using Hst.Amiga.FileSystems.FastFileSystem;
using System.Buffers.Binary;
using System.Text;

namespace AmiDiskLab.Tests;

public class BootableA500Tests
{
    // A minimal 68k Hunk program: one code hunk containing RTS and NOP.
    private static readonly byte[] MinimalHunk = Convert.FromHexString(
        "000003F30000000000000001000000000000000000000001000003E9000000014E754E71000003F2");

    [Fact]
    public async Task BootImageContainsStandardBootCodeAndStartupProgram()
    {
        using var folder = new TestFolder();
        var source = folder.File("source");
        Directory.CreateDirectory(Path.Combine(source, "Game"));
        var program = Path.Combine(source, "Game", "START");
        await File.WriteAllBytesAsync(program, MinimalHunk);
        var output = folder.File("boot.adf");
        await new AmigaDiskImageWriter().CreateBootableA500AdfFromFolderAsync(
            source, output, "A500TEST", "Game/START");

        var image = await File.ReadAllBytesAsync(output);
        Assert.Equal(901120, image.Length);
        Assert.Equal(new byte[] { (byte)'D', (byte)'O', (byte)'S', 0 }, image[..4]);
        Assert.Contains(image.AsSpan(12, 80).ToArray(), value => value != 0);
        Assert.Equal(uint.MaxValue, BootBlockSum(image));
        using var stream = File.OpenRead(output);
        using var volume = await FastFileSystemVolume.MountAdf(stream);
        await volume.ChangeDirectory("/Game");
        await using (var file = await volume.OpenFile("START", Hst.Amiga.FileSystems.FileMode.Read))
        {
            using var actual = new MemoryStream();
            await file.CopyToAsync(actual);
            Assert.Equal(MinimalHunk, actual.ToArray());
        }
        await volume.ChangeDirectory("/S");
        await using (var file = await volume.OpenFile("Startup-Sequence", Hst.Amiga.FileSystems.FileMode.Read))
        {
            using var actual = new MemoryStream();
            await file.CopyToAsync(actual);
            Assert.Equal("SYS:Game/START\n", Encoding.ASCII.GetString(actual.ToArray()));
        }
    }

    [Fact]
    public async Task ExistingStartupSequenceIsNeverReplaced()
    {
        using var folder = new TestFolder();
        var source = folder.File("source");
        Directory.CreateDirectory(Path.Combine(source, "S"));
        await File.WriteAllBytesAsync(Path.Combine(source, "START"), MinimalHunk);
        await File.WriteAllTextAsync(Path.Combine(source, "S", "Startup-Sequence"), "old");
        var output = folder.File("existing.adf");
        await File.WriteAllBytesAsync(output, [42]);
        await Assert.ThrowsAsync<IOException>(() => new AmigaDiskImageWriter()
            .CreateBootableA500AdfFromFolderAsync(source, output, "TEST", "START"));
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task NonExecutableIsRejectedBeforeOutputIsTouched()
    {
        using var folder = new TestFolder();
        var source = folder.File("source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(Path.Combine(source, "DATA"), [1, 2, 3, 4]);
        var output = folder.File("existing.adf");
        await File.WriteAllBytesAsync(output, [42]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new AmigaDiskImageWriter()
            .CreateBootableA500AdfFromFolderAsync(source, output, "TEST", "DATA"));
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task TruncatedHunkHeaderIsRejected()
    {
        using var folder = new TestFolder();
        var source = folder.File("source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(Path.Combine(source, "BROKEN"), [0, 0, 3, 0xF3]);
        var output = folder.File("existing.adf");
        await File.WriteAllBytesAsync(output, [42]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new AmigaDiskImageWriter()
            .CreateBootableA500AdfFromFolderAsync(source, output, "TEST", "BROKEN"));
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task WhdLoadArchiveReportsA500IncompatibilityWithoutChangingOutput()
    {
        using var folder = new TestFolder();
        var output = folder.File("existing.adf");
        await File.WriteAllBytesAsync(output, [42]);
        var converter = new ArchiveToAdfConverter(new LhaArchiveExtractor(), new AmigaDiskImageWriter());
        var archive = await SyntheticLhaArchive.CreateAsync(folder, whdLoad: true);
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => converter.ConvertBootableA500Async(
            archive, output, "SAMPLE"));
        Assert.Contains("WHDLoad", exception.Message);
        Assert.Contains("Kickstart 1.3", exception.Message);
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task WhdLoadArchiveWithKickstart20ReportsExportLimitWithoutClaimingKickstartIncompatibility()
    {
        using var folder = new TestFolder();
        var converter = new ArchiveToAdfConverter(new LhaArchiveExtractor(), new AmigaDiskImageWriter());
        var archive = await SyntheticLhaArchive.CreateAsync(folder, whdLoad: true);
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => converter.ConvertBootableA500Async(
            archive,
            folder.File("game.adf"), "GAME", targetSystem: new(
                AmiDiskLab.Core.Models.KickstartVersion.V20, 1)));
        Assert.Contains("Kickstart 2.0", exception.Message);
        Assert.Contains("Automatic Gotek boot ADF", exception.Message);
        Assert.Contains("No complete 880 KiB disk image", exception.Message);
        Assert.DoesNotContain("also requires Kickstart 2.0", exception.Message);
        Assert.False(File.Exists(folder.File("game.adf")));
    }

    [Fact]
    public async Task EmbeddedDiskExportCopiesRawDiskBytesWithoutOverwriting()
    {
        using var folder = new TestFolder();
        var source = folder.File("package");
        Directory.CreateDirectory(Path.Combine(source, "Game"));
        var disk = new byte[901_120];
        disk[0] = (byte)'D';
        disk[1] = (byte)'O';
        disk[2] = (byte)'S';
        disk[13] = 42;
        await File.WriteAllBytesAsync(Path.Combine(source, "Game", "Disk.1"), disk);
        await File.WriteAllBytesAsync(Path.Combine(source, "Game", "Game.Slave"), [1, 2, 3]);
        var archive = folder.File("game.lha");
        Assert.Equal(LHAWriteResult.Success, LHAWriter.WriteLHAFile(archive, source,
            compressionMethod: CompressionMethod.None, includeEmptyDirectories: true));
        var destination = folder.File("output");
        var converter = new ArchiveToAdfConverter(new LhaArchiveExtractor(), new AmigaDiskImageWriter());
        var bootError = await Assert.ThrowsAsync<NotSupportedException>(() => converter.ConvertBootableA500Async(
            archive, folder.File("boot.adf"), "GAME", targetSystem: new(
                AmiDiskLab.Core.Models.KickstartVersion.V20, 1)));
        Assert.Contains("Extract disk ADFs", bootError.Message);
        var result = await converter.ExportEmbeddedDiskImagesAsync(archive, destination);
        Assert.Equal(1, result.DiskCount);
        var output = Assert.Single(Directory.GetFiles(destination));
        Assert.Equal(disk, await File.ReadAllBytesAsync(output));
        await Assert.ThrowsAsync<IOException>(() => converter.ExportEmbeddedDiskImagesAsync(archive, destination));
        Assert.Equal(disk, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task LhaWithOneHunkProgramProducesA500BootAdf()
    {
        using var folder = new TestFolder();
        var source = folder.File("package");
        Directory.CreateDirectory(Path.Combine(source, "Game"));
        await File.WriteAllBytesAsync(Path.Combine(source, "Game", "START"), MinimalHunk);
        await File.WriteAllBytesAsync(Path.Combine(source, "Game", "DATA"), [1, 2, 3]);
        var archive = folder.File("program.lha");
        Assert.Equal(LHAWriteResult.Success, LHAWriter.WriteLHAFile(archive, source,
            compressionMethod: CompressionMethod.None, includeEmptyDirectories: true));
        var output = folder.File("program.adf");
        await new ArchiveToAdfConverter(new LhaArchiveExtractor(), new AmigaDiskImageWriter())
            .ConvertBootableA500Async(archive, output, "GAME");
        var image = await File.ReadAllBytesAsync(output);
        Assert.Equal(new byte[] { 68, 79, 83, 0 }, image[..4]);
        Assert.Equal(uint.MaxValue, BootBlockSum(image));
        using var stream = File.OpenRead(output);
        using var volume = await FastFileSystemVolume.MountAdf(stream);
        await volume.ChangeDirectory("/Game");
        await using (var file = await volume.OpenFile("START", Hst.Amiga.FileSystems.FileMode.Read))
        {
            using var actual = new MemoryStream();
            await file.CopyToAsync(actual);
            Assert.Equal(MinimalHunk, actual.ToArray());
        }
        await volume.ChangeDirectory("/S");
        Assert.Contains(await volume.ListEntries(), entry => entry.Name.Equals("Startup-Sequence", StringComparison.OrdinalIgnoreCase));
    }

    private static uint BootBlockSum(byte[] image)
    {
        ulong sum = 0;
        for (var offset = 0; offset < 1024; offset += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(offset));
            if (sum > uint.MaxValue) sum = (sum & uint.MaxValue) + 1;
        }
        return (uint)sum;
    }
}

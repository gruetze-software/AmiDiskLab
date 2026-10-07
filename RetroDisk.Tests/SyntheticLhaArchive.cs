using Amiga.FileFormats.LHA;
using System.Text;

namespace AmiDiskLab.Tests;

internal static class SyntheticLhaArchive
{
    public static async Task<string> CreateAsync(TestFolder folder, bool whdLoad = false)
    {
        var source = folder.File("synthetic-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(source, "Docs"));
        await File.WriteAllBytesAsync(Path.Combine(source, "Sample.info"), [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.Combine(source, "Docs", "ReadMe"),
            "This install applies to \"Synthetic Game\" © 1992 Example Studio.\n" +
            "Type: game/demo\nThe installed game requires an Amiga.\n",
            Encoding.Latin1);
        if (whdLoad)
            await File.WriteAllBytesAsync(Path.Combine(source, "Synthetic.Slave"), [0, 1, 2, 3]);
        var archive = folder.File("synthetic-" + Guid.NewGuid().ToString("N") + ".lha");
        var result = LHAWriter.WriteLHAFile(archive, source, compressionMethod: CompressionMethod.None,
            includeEmptyDirectories: true);
        Assert.Equal(LHAWriteResult.Success, result);
        return archive;
    }
}

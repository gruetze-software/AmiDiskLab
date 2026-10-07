using AmiDiskLab.App;
using AmiDiskLab.Infrastructure.Metadata;
using System.Text;

namespace AmiDiskLab.Tests;

public class ScreenScraperAccessStoreTests
{
    [Fact]
    public void CredentialsRoundTripEncryptedAndCanBeRemoved()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var folder = new TestFolder();
        var path = folder.File("screenscraper-credentials.dat");
        var store = new ScreenScraperAccessStore(path);
        var access = new ScreenScraperAccess("user-id", "user-secret-456");

        store.Save(access);

        Assert.Equal(access, new ScreenScraperAccessStore(path).Load());
        var raw = File.ReadAllBytes(path);
        Assert.DoesNotContain("user-secret-456", Encoding.UTF8.GetString(raw));

        store.Save(null);
        Assert.False(File.Exists(path));
        Assert.Null(store.Load());
    }

    [Fact]
    public void CorruptedCredentialFileIsIgnored()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var folder = new TestFolder();
        var path = folder.File("screenscraper-credentials.dat");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        Assert.Null(new ScreenScraperAccessStore(path).Load());
    }
}

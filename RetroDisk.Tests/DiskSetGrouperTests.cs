using AmiDiskLab.Core.Models;

namespace AmiDiskLab.Tests;

public class DiskSetGrouperTests
{
    [Fact]
    public void AmbermoonDisksBecomeOneGameWithoutMergingOtherFolders()
    {
        var items = Enumerable.Range(0, 9).Select(index => Disk(
            $"Ambermoon v1.01 ({(char)('A' + index)}).adf", "Games/Ambermoon")).ToList();
        items.Add(Disk("Ambermoon v1.01 (A).adf", "Backup/Ambermoon"));
        items.Add(Disk("Another Game.adf", "Games/Ambermoon"));

        var grouped = DiskSetGrouper.Group(items);

        Assert.Equal(3, grouped.Count);
        var game = grouped[0];
        Assert.Equal(9, game.DiskCount);
        Assert.Equal("Ambermoon v1.01", game.DiskSetTitle);
        Assert.Equal("Ambermoon v1.01 (9 disks)", game.DisplayTitle);
        Assert.Equal(9 * 901_120, game.FileSize);
        Assert.EndsWith("(A).adf", game.FullPath);
        Assert.Equal(9, game.AllPaths.Count);
        Assert.Single(grouped, item => item.DiskCount == 1 && item.FileName.Contains("Ambermoon"));
    }

    [Fact]
    public void DuplicateDiskLettersAreNotGrouped()
    {
        var items = new[]
        {
            Disk("Game (A).adf", "One"), Disk("Game (A).adf", "One"),
            Disk("Game (B).adf", "One")
        };
        Assert.Equal(3, DiskSetGrouper.Group(items).Count);
    }

    [Fact]
    public void NumberedDisksWithDifferentTrailingTagsFormOneSet()
    {
        var items = new[]
        {
            Disk("Siedler, Die (Disk 1 of 3)(Intro).adf", "Siedler"),
            Disk("Siedler, Die (Disk 2 of 3).adf", "Siedler"),
            Disk("Siedler, Die (Disk 3 of 3)(bootable).adf", "Siedler")
        };

        var game = Assert.Single(DiskSetGrouper.Group(items));

        Assert.Equal("Siedler, Die", game.DiskSetTitle);
        Assert.Equal(3, game.DiskCount);
        Assert.Equal(items.Select(item => item.FullPath), game.AllPaths);
        Assert.Equal("Siedler, Die", DiskSetGrouper.BaseTitle(items[0].FileName));
    }

    [Fact]
    public void ConflictingDiskTotalsAreNotMerged()
    {
        var items = new[]
        {
            Disk("Game (Disk 1 of 2).adf", "Game"),
            Disk("Game (Disk 2 of 3).adf", "Game")
        };
        Assert.Equal(2, DiskSetGrouper.Group(items).Count);
    }

    [Fact]
    public void DiscSpellingWithTotalFormsOneSet()
    {
        var items = new[]
        {
            Disk("Alcatraz (Disc 1 of 2).adf", "Demos"),
            Disk("Alcatraz (Disc 2 of 2).adf", "Demos")
        };

        var demo = Assert.Single(DiskSetGrouper.Group(items));

        Assert.Equal("Alcatraz", demo.DiskSetTitle);
        Assert.Equal(2, demo.DiskCount);
        Assert.Equal("Alcatraz", DiskSetGrouper.BaseTitle(items[0].FileName));
    }

    [Theory]
    [InlineData("Demo 1-2.adf", "Demo 2-2.adf")]
    [InlineData("Demo_1-2.adf", "Demo_2-2.adf")]
    [InlineData("Demo-1-2.adf", "Demo-2-2.adf")]
    public void ShortNumberAndTotalSuffixFormsOneSet(string first, string second)
    {
        var demo = Assert.Single(DiskSetGrouper.Group(new[]
        {
            Disk(first, "Demos"), Disk(second, "Demos")
        }));

        Assert.Equal("Demo", demo.DiskSetTitle);
        Assert.Equal(2, demo.DiskCount);
    }

    [Fact]
    public void ShortSuffixWithConflictingTotalsIsNotGrouped()
    {
        var items = new[]
        {
            Disk("Demo 1-2.adf", "Demos"), Disk("Demo 2-3.adf", "Demos")
        };

        Assert.Equal(2, DiskSetGrouper.Group(items).Count);
    }

    [Fact]
    public void CompactTwoDigitSuffixFormsOneSet()
    {
        var items = new[]
        {
            Disk("Name01.adf", "Demos"), Disk("Name02.adf", "Demos"),
            Disk("Name03.adf", "Demos")
        };

        var demo = Assert.Single(DiskSetGrouper.Group(items));

        Assert.Equal("Name", demo.DiskSetTitle);
        Assert.Equal(3, demo.DiskCount);
        Assert.Equal(items.Select(item => item.FullPath), demo.AllPaths);
        Assert.Null(DiskSetGrouper.BaseTitle(items[0].FileName));
    }

    [Fact]
    public void CompactSuffixMustBeContinuousAndStartAtOne()
    {
        var missingFirst = new[]
        {
            Disk("Name02.adf", "Demos"), Disk("Name03.adf", "Demos")
        };
        var gap = new[]
        {
            Disk("Name01.adf", "Demos"), Disk("Name03.adf", "Demos")
        };

        Assert.Equal(2, DiskSetGrouper.Group(missingFirst).Count);
        Assert.Equal(2, DiskSetGrouper.Group(gap).Count);
    }

    [Fact]
    public void ReleaseYearIsNotMistakenForCompactDiskNumber()
    {
        Assert.Null(DiskSetGrouper.BaseTitle("Alien_Breed_1991.adf"));
        Assert.Equal(2, DiskSetGrouper.Group(new[]
        {
            Disk("Demo_1991.adf", "Demos"), Disk("Demo_1992.adf", "Demos")
        }).Count);
    }

    [Fact]
    public void CompactSuffixDoesNotMixWithExplicitDiskNotation()
    {
        var items = new[]
        {
            Disk("Name01.adf", "Demos"), Disk("Name (Disk 2).adf", "Demos")
        };

        Assert.Equal(2, DiskSetGrouper.Group(items).Count);
    }

    [Theory]
    [InlineData("Name-1.adf", "Name-2.adf")]
    [InlineData("Name_1.adf", "Name_2.adf")]
    [InlineData("Name 1.adf", "Name 2.adf")]
    public void SeparatedNumberSuffixFormsOneSet(string first, string second)
    {
        var set = Assert.Single(DiskSetGrouper.Group(new[]
        {
            Disk(first, "Demos"), Disk(second, "Demos")
        }));

        Assert.Equal("Name", set.DiskSetTitle);
        Assert.Equal(2, set.DiskCount);
    }

    [Fact]
    public void SimilarSequelNameIsNotIncludedInSeparatedNumberSet()
    {
        var items = new[]
        {
            Disk("Crazycar-1.adf", "Games"), Disk("Crazycar-2.adf", "Games"),
            Disk("crazycars2.adf", "Games")
        };

        var grouped = DiskSetGrouper.Group(items);

        Assert.Equal(2, grouped.Count);
        Assert.Equal(2, grouped.Single(item => item.DiskCount == 2).DiskCount);
        Assert.Contains(grouped, item => item.DiskCount == 1 && item.FileName == "crazycars2.adf");
    }

    private static RetroSoftwareItem Disk(string fileName, string folder) => new()
    {
        FileName = fileName,
        FullPath = Path.GetFullPath(Path.Combine(folder, fileName)),
        RelativePath = folder,
        FileSize = 901_120,
        Format = SoftwareFormat.Adf,
        Type = SoftwareItemType.DiskImage
    };
}

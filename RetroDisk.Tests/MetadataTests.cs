using AmiDiskLab.App;
using AmiDiskLab.Core.Models;
using AmiDiskLab.Infrastructure.Archives;
using AmiDiskLab.Infrastructure.FileSystem;
using AmiDiskLab.Infrastructure.Metadata;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace AmiDiskLab.Tests;

public class MetadataTests
{
    [Fact]
    public async Task SyntheticReadmeSuggestsGameTitleAndStudio()
    {
        using var folder = new TestFolder();
        var archive = await SyntheticLhaArchive.CreateAsync(folder);
        var suggestion = await ReadmeMetadataSuggester.SuggestAsync(archive);
        Assert.NotNull(suggestion);
        Assert.Equal("Synthetic Game", suggestion.Title);
        Assert.Equal("Example Studio", suggestion.Studio);
        Assert.Equal(SoftwareCategory.Game, suggestion.Category);
    }

    [Fact]
    public void MetadataPersistsWithoutTouchingSoftwareFile()
    {
        using var folder = new TestFolder();
        var software = folder.File("game.adf");
        File.WriteAllBytes(software, [1, 2, 3]);
        var catalog = folder.File("config/metadata.json");
        new SoftwareMetadataStore(catalog).Set(software,
            new SoftwareMetadata("A Game", "A Studio", SoftwareCategory.Game, "1992-03-04",
                "A Publisher", "A description", "https://www.screenscraper.fr/image.php?gameid=1",
                Genre: "Platformer"));
        var loaded = new SoftwareMetadataStore(catalog).Get(software);
        Assert.Equal("A Game", loaded.Title);
        Assert.Equal("A Studio", loaded.Studio);
        Assert.Equal(SoftwareCategory.Game, loaded.Category);
        Assert.Equal("1992-03-04", loaded.ReleaseDate);
        Assert.Equal("A Publisher", loaded.Publisher);
        Assert.Equal("A description", loaded.Description);
        Assert.Equal("Platformer", loaded.Genre);
        Assert.Contains("image.php", loaded.CoverUrl);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(software));
    }

    [Fact]
    public void OlderMetadataCatalogStillLoads()
    {
        using var folder = new TestFolder();
        var catalog = folder.File("metadata.json");
        var path = folder.File("old.adf");
        File.WriteAllText(catalog, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [path] = new { Title = "Old title", Studio = "Old studio", Category = 1 }
        }));
        var metadata = new SoftwareMetadataStore(catalog).Get(path);
        Assert.Equal("Old title", metadata.Title);
        Assert.Null(metadata.ReleaseDate);
    }

    [Fact]
    public void AmbiguousCopyrightCreditIsNotTreatedAsStudio()
    {
        var suggestion = ReadmeMetadataSuggester.Parse(
            "Type: game/patch\nThis patch applies to \"Batman Returns\" (c) 1993 Konami/Gametek/Dentons - 2 disks.");
        Assert.NotNull(suggestion);
        Assert.Equal("Batman Returns", suggestion.Title);
        Assert.Null(suggestion.Studio);
        Assert.Equal(SoftwareCategory.Game, suggestion.Category);
    }

    [Fact]
    public void AlienBreedFileNameSuggestsDemoAndTeam17()
    {
        var suggestion = AdfMetadataSuggester.SuggestFromFileName(
            "Alien_Breed_3D_demo-playable_1995_Team_17_Amiga_Format_AGA");
        Assert.NotNull(suggestion);
        Assert.Equal("Alien Breed 3D", suggestion.Metadata.Title);
        Assert.Equal("Team 17", suggestion.Metadata.Studio);
        Assert.Equal("1995", suggestion.Metadata.ReleaseDate);
        Assert.Equal(SoftwareCategory.Demo, suggestion.Metadata.Category);
        Assert.Contains("file name", suggestion.Source);
    }

    [Fact]
    public async Task CompanionRp9OverridesFilenameSuggestion()
    {
        using var folder = new TestFolder();
        var adf = folder.File("Arnie_1992_Zeppelin.adf");
        File.WriteAllBytes(adf, [1]);
        using (var zip = ZipFile.Open(folder.File("Arnie.rp9"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("rp9-manifest.xml").Open()))
            writer.Write("<rp9><application><description><type>game</type>" +
                "<entity type=\"publisher\">Zeppelin Platinum</entity><title>Arnie</title>" +
                "</description><media><floppy>Arnie_1992_Zeppelin.adf</floppy></media>" +
                "</application></rp9>");
        var suggestion = await AdfMetadataSuggester.SuggestAsync(adf);
        Assert.NotNull(suggestion);
        Assert.Equal("Arnie", suggestion.Metadata.Title);
        Assert.Null(suggestion.Metadata.Studio);
        Assert.Equal("Zeppelin Platinum", suggestion.Metadata.Publisher);
        Assert.Equal(SoftwareCategory.Game, suggestion.Metadata.Category);
        Assert.StartsWith("RP9:", suggestion.Source);
    }

    [Fact]
    public async Task ScreenScraperRequiresAnExactRomHashAndSize()
    {
        using var folder = new TestFolder();
        var adf = folder.File("Game.adf");
        await File.WriteAllBytesAsync(adf, [1, 2, 3, 4]);
        var hash = Convert.ToHexString(SHA1.HashData([1, 2, 3, 4]));
        var handler = new StubHandler($"<Data><jeu><nom>Game</nom><developpeur>Studio</developpeur>" +
            $"<rom><romsha1>{hash}</romsha1><romsize>4</romsize><demo>0</demo></rom>" +
            "</jeu></Data>");
        using var http = new HttpClient(handler);
        var client = new ScreenScraperClient(http);
        var access = new ScreenScraperAccess("user", "user-secret");
        var suggestion = await client.LookupAdfAsync(adf, access);
        Assert.NotNull(suggestion);
        Assert.Equal("Game", suggestion.Metadata.Title);
        Assert.Equal("Studio", suggestion.Metadata.Studio);
        Assert.Equal(SoftwareCategory.Game, suggestion.Metadata.Category);
        Assert.Contains("sha1=" + hash, handler.RequestUri!.Query);
        Assert.Contains("md5=" + Convert.ToHexString(MD5.HashData([1, 2, 3, 4])), handler.RequestUri.Query);
        Assert.Contains("crc=B63CFBCD", handler.RequestUri.Query);
        Assert.Contains("romtaille=4", handler.RequestUri.Query);
        handler.Xml = "<Data><jeu><nom>Wrong Game</nom><rom><romsha1>ABC</romsha1>" +
            "<romsize>4</romsize></rom></jeu></Data>";
        Assert.Null(await client.LookupAdfAsync(adf, access));
    }

    [Fact]
    public async Task ScreenScraperSearchOffersMultipleUnverifiedNameMatches()
    {
        using var folder = new TestFolder();
        var adf = folder.File("Alien_Breed_1991.adf");
        await File.WriteAllBytesAsync(adf, [1, 2, 3, 4]);
        var handler = new StubHandler("<Data><jeu><nom>Unrelated</nom></jeu></Data>");
        using var http = new HttpClient(handler);
        var client = new ScreenScraperClient(http);
        handler.SearchXml = "<Data><jeux><jeu><nom>Alien Breed</nom><editeur>Team17</editeur></jeu>" +
            "<jeu><nom>Alien Breed II</nom><editeur>Team17</editeur></jeu></jeux></Data>";
        var matches = await client.SearchAdfAsync(adf,
            new ScreenScraperAccess("user", "user-secret"));
        Assert.Equal(2, matches.Count);
        Assert.Equal("Alien Breed", matches[0].Metadata.Title);
        Assert.Equal("Alien Breed II", matches[1].Metadata.Title);
        Assert.All(matches, match => Assert.Contains("not verified", match.Source));
        Assert.Contains("recherche=Alien%20Breed", handler.RequestUri!.Query);
    }

    [Fact]
    public async Task ScreenScraperNameFallbackRemovesAllParentheticalFileTags()
    {
        using var folder = new TestFolder();
        var adf = folder.File("Puggsy (1994)(Psygnosis)(Disk 4 of 4).adf");
        await File.WriteAllBytesAsync(adf, [1, 2, 3, 4]);
        var handler = new StubHandler("<Data />")
        {
            SearchXml = "<Data><jeux><jeu><nom>Puggsy</nom><editeur>Psygnosis</editeur></jeu></jeux></Data>"
        };
        using var http = new HttpClient(handler);

        var matches = await new ScreenScraperClient(http).SearchAdfAsync(adf,
            new ScreenScraperAccess("user", "user-secret"));

        var match = Assert.Single(matches);
        Assert.Equal("Puggsy", match.Metadata.Title);
        Assert.Contains("recherche=Puggsy", handler.RequestUri!.Query);
        Assert.DoesNotContain("1994", handler.RequestUri.Query);
        Assert.DoesNotContain("Psygnosis", handler.RequestUri.Query);
        Assert.Equal("Puggsy", ScreenScraperClient.RemoveParentheticalFileTags(
            "Puggsy (1994)(Psygnosis)(Disk 4 of 4)"));
    }

    [Fact]
    public async Task ScreenScraperPreservesSequelNumberAndRetriesWithoutSubtitle()
    {
        using var folder = new TestFolder();
        var adf = folder.File("Rainbow Islands - The Story of Bubble Bobble 2.adf");
        await File.WriteAllBytesAsync(adf, [1, 2, 3, 4]);
        var handler = new StubHandler("<Data />")
        {
            SearchResponse = uri => uri.Query.EndsWith("recherche=Rainbow%20Islands", StringComparison.Ordinal)
                ? "<Data><jeux><jeu><nom>Rainbow Islands</nom><editeur>Ocean</editeur></jeu></jeux></Data>"
                : "<Data><jeux /></Data>"
        };
        using var http = new HttpClient(handler);

        var matches = await new ScreenScraperClient(http).SearchAdfAsync(adf,
            new ScreenScraperAccess("user", "user-secret"));

        Assert.Single(matches);
        Assert.Equal(2, handler.SearchRequestUris.Count);
        Assert.Contains("recherche=Rainbow%20Islands%20-%20The%20Story%20of%20Bubble%20Bobble%202",
            handler.SearchRequestUris[0].Query);
        Assert.Contains("recherche=Rainbow%20Islands", handler.SearchRequestUris[1].Query);
        Assert.Equal(new[] { "Rainbow Islands - The Story of Bubble Bobble 2", "Rainbow Islands" },
            ScreenScraperClient.NameSearchCandidates("Rainbow Islands - The Story of Bubble Bobble 2"));
    }

    [Fact]
    public void ScreenScraperReadsRegionalTitleDateDescriptionPublisherAndCover()
    {
        const string hash = "0123456789ABCDEF0123456789ABCDEF01234567";
        var xml = $"""
            <Data><jeu>
              <noms><nom region="us">US Title</nom><nom region="eu">European Title</nom></noms>
              <developpeur>Developer</developpeur><editeur>Publisher</editeur>
              <note>18</note>
              <editeurmedias>
                <editeurmedia_pictocouleur>https://www.screenscraper.fr/company-logo.png</editeurmedia_pictocouleur>
              </editeurmedias>
              <dates><date region="us">1988-01-01</date><date region="eu">1987-12-01</date></dates>
              <synopsis><synopsis langue="en">English description</synopsis>
                        <synopsis langue="de">Deutsche Beschreibung</synopsis></synopsis>
              <genres><genre langue="en">Platform</genre><genre langue="de">Jump ’n’ Run</genre></genres>
              <medias><media type="box-2D" region="eu">https://www.screenscraper.fr/image.php?gameid=1</media>
                      <media type="ss" region="eu">https://www.screenscraper.fr/image.php?gameid=1&amp;type=ss</media></medias>
              <rom><romsha1>{hash}</romsha1><romsize>901120</romsize></rom>
            </jeu></Data>
            """;
        var suggestion = ScreenScraperClient.ParseExactMatch(System.Xml.Linq.XDocument.Parse(xml), hash, 901_120);
        Assert.NotNull(suggestion);
        Assert.Equal("European Title", suggestion.Metadata.Title);
        Assert.Equal("Developer", suggestion.Metadata.Studio);
        Assert.Equal("Publisher", suggestion.Metadata.Publisher);
        Assert.Contains("company-logo.png", suggestion.Metadata.PublisherLogoUrl);
        Assert.Equal("1987-12-01", suggestion.Metadata.ReleaseDate);
        Assert.Equal("Deutsche Beschreibung", suggestion.Metadata.Description);
        Assert.Equal("Jump ’n’ Run", suggestion.Metadata.Genre);
        Assert.Equal(4.5, suggestion.Metadata.Rating);
        Assert.Contains("image.php", suggestion.Metadata.CoverUrl);
        Assert.Contains("type=ss", suggestion.Metadata.ScreenshotUrl);
    }

    [Fact]
    public async Task ScreenScraperRequestsProtectedPublisherLogoFromCompanyId()
    {
        using var folder = new TestFolder();
        var adf = folder.File("Game.adf");
        await File.WriteAllBytesAsync(adf, [1, 2, 3, 4]);
        var hash = Convert.ToHexString(SHA1.HashData([1, 2, 3, 4]));
        var xml = $"""
            <Data><jeu><nom>Game</nom><editeur id="42">Publisher</editeur>
              <rom><romsha1>{hash}</romsha1><romsize>4</romsize></rom>
            </jeu></Data>
            """;
        var handler = new StubHandler(xml);
        using var http = new HttpClient(handler);

        var suggestion = await new ScreenScraperClient(http).LookupAdfAsync(adf,
            new ScreenScraperAccess("user", "user-secret"));

        Assert.NotNull(suggestion);
        Assert.Equal(ScreenScraperClient.ProxyRoot + "/v1/media?token=test-token",
            suggestion.Metadata.PublisherLogoUrl);
        Assert.True(handler.MediaTokenRequested);
    }

    [Fact]
    public void ScreenScraperReadsGenericPublisherMediaElement()
    {
        const string hash = "0123456789ABCDEF0123456789ABCDEF01234567";
        var xml = $"""
            <Data><jeu><nom>Game</nom><editeur>Publisher</editeur>
              <editeurmedias><media type="pictocouleur" url="https://api.screenscraper.fr/company/42.png" /></editeurmedias>
              <rom><romsha1>{hash}</romsha1><romsize>901120</romsize></rom>
            </jeu></Data>
            """;

        var suggestion = ScreenScraperClient.ParseExactMatch(System.Xml.Linq.XDocument.Parse(xml), hash, 901_120);

        Assert.Equal("https://api.screenscraper.fr/company/42.png", suggestion?.Metadata.PublisherLogoUrl);
    }

    [Fact]
    public void ScreenScraperPrefersNestedColorPublisherMediaOverMonochrome()
    {
        const string hash = "0123456789ABCDEF0123456789ABCDEF01234567";
        var xml = $"""
            <Data><jeu><nom>Game</nom><editeur>Publisher</editeur>
              <wrapper><editeurmedias>
                <media type="pictomonochrome" url="https://api.screenscraper.fr/company/mono.png" />
                <group><media type="pictocouleur" url="https://api.screenscraper.fr/company/color.png" /></group>
              </editeurmedias></wrapper>
              <rom><romsha1>{hash}</romsha1><romsize>901120</romsize></rom>
            </jeu></Data>
            """;

        var suggestion = ScreenScraperClient.ParseExactMatch(System.Xml.Linq.XDocument.Parse(xml), hash, 901_120);

        Assert.Equal("https://api.screenscraper.fr/company/color.png", suggestion?.Metadata.PublisherLogoUrl);
    }

    [Fact]
    public void ScreenScraperRespectsLanguageAndRegionAndReadsAuthenticatedMediaLinks()
    {
        const string hash = "0123456789ABCDEF0123456789ABCDEF01234567";
        var xml = $"""
            <Data><jeu>
              <noms><nom region="eu">European title</nom><nom region="us">US title</nom></noms>
              <dates><date region="eu">1991-01-01</date><date region="us">1992-01-01</date></dates>
              <synopsis><synopsis langue="en">English</synopsis><synopsis langue="fr">Français</synopsis></synopsis>
              <medias><media type="box-2D" region="eu">https://api.screenscraper.fr/api2/mediaJeu.php?devid=a&amp;devpassword=secret&amp;media=box-2D</media>
                      <media type="box-2D" region="us">https://api.screenscraper.fr/api2/mediaJeu.php?devid=a&amp;devpassword=secret&amp;media=box-2D-us</media>
                      <media type="ss">https://api.screenscraper.fr/api2/mediaJeu.php?devid=a&amp;devpassword=secret&amp;media=ss</media></medias>
              <rom><romsha1>{hash}</romsha1><romsize>901120</romsize></rom>
            </jeu></Data>
            """;
        var match = ScreenScraperClient.ParseExactMatch(System.Xml.Linq.XDocument.Parse(xml), hash,
            901_120, new ScreenScraperPreferences("fr", "us"));
        Assert.NotNull(match);
        Assert.Equal("US title", match.Metadata.Title);
        Assert.Equal("1992-01-01", match.Metadata.ReleaseDate);
        Assert.Equal("Français", match.Metadata.Description);
        Assert.Contains("box-2D-us", match.Metadata.CoverUrl);
        Assert.Contains("media=ss", match.Metadata.ScreenshotUrl);
    }

    [Fact]
    public async Task ScreenScraperFallsBackToFileNameWhenApiOmitsTitle()
    {
        using var folder = new TestFolder();
        var adf = folder.File("Barbarian_1987_Psygnosis_cr_TOC.adf");
        await File.WriteAllBytesAsync(adf, [1, 2, 3, 4]);
        var hash = Convert.ToHexString(SHA1.HashData([1, 2, 3, 4]));
        var handler = new StubHandler($"<Data><jeu><editeur>Psygnosis</editeur>" +
            $"<rom><romsha1>{hash}</romsha1><romsize>4</romsize></rom></jeu></Data>");
        using var http = new HttpClient(handler);
        var suggestion = await new ScreenScraperClient(http).LookupAdfAsync(adf,
            new ScreenScraperAccess("user", "user-secret"));
        Assert.NotNull(suggestion);
        Assert.Equal("Barbarian", suggestion.Metadata.Title);
        Assert.Equal("1987", suggestion.Metadata.ReleaseDate);
        Assert.Equal("Psygnosis", suggestion.Metadata.Publisher);
        Assert.Contains("file name", suggestion.Source);
    }

    private sealed class StubHandler(string xml) : HttpMessageHandler
    {
        public string Xml { get; set; } = xml;
        public string? SearchXml { get; set; }
        public Uri? RequestUri { get; private set; }
        public Func<Uri, string?>? SearchResponse { get; set; }
        public List<Uri> SearchRequestUris { get; } = [];
        public bool MediaTokenRequested { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var endpoint = document.RootElement.GetProperty("endpoint").GetString()!;
            var parameters = document.RootElement.GetProperty("parameters").EnumerateObject()
                .Select(property => Uri.EscapeDataString(property.Name) + "=" +
                    Uri.EscapeDataString(property.Value.GetString() ?? string.Empty));
            var logicalUri = new Uri("https://proxy.test/" + endpoint + "?" + string.Join("&", parameters));
            RequestUri = logicalUri;
            if (request.RequestUri!.AbsolutePath.Equals("/v1/media-token", StringComparison.Ordinal))
            {
                MediaTokenRequested = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"url\":\"" + ScreenScraperClient.ProxyRoot +
                        "/v1/media?token=test-token\"}", System.Text.Encoding.UTF8, "application/json")
                };
            }
            var search = endpoint.Equals("jeuRecherche.php", StringComparison.OrdinalIgnoreCase);
            if (search) SearchRequestUris.Add(logicalUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(search ? SearchResponse?.Invoke(logicalUri) ?? SearchXml ?? Xml : Xml) };
        }
    }
}

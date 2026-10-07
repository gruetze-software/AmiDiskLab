using AmiDiskLab.Core.Models;
using AmiDiskLab.Infrastructure.Metadata;
using System.Net;
using System.Net.Http;

namespace AmiDiskLab.Tests;

public class DemozooClientTests
{
    [Theory]
    [InlineData("State_of_the_Art_1992_Spaceballs.adf", "State of the Art")]
    [InlineData("Siedler, Die (Disk 1 of 3)(Intro).adf", "Siedler, Die")]
    public void SearchTitleRemovesDiskAndArchiveTags(string fileName, string expected) =>
        Assert.Equal(expected, DemozooClient.SearchTitle(fileName));

    [Fact]
    public async Task SearchKeepsOnlyAmigaProductionsAndSceneFields()
    {
        var handler = new StubHandler("""
            {"count":2,"next":null,"results":[
              {"id":1,"title":"State of the Art","release_date":"1992","demozoo_url":"https://demozoo.org/productions/1/",
               "platforms":[{"name":"Amiga OCS/ECS"}],"types":[{"name":"Demo"}],
               "author_nicks":[{"releaser":{"name":"Spaceballs","is_group":true}}],
               "screenshots":[{"standard_url":"https://demozoo.org/media/screenshots/1.png"}]},
              {"id":2,"title":"State of the Art","platforms":[{"name":"Windows"}]}
            ]}
            """);
        using var http = new HttpClient(handler);
        var matches = await new DemozooClient(http).SearchAsync("State of the Art");
        var match = Assert.Single(matches);
        Assert.Equal("State of the Art", match.Metadata.Title);
        Assert.Equal("Spaceballs", match.Metadata.SceneGroup);
        Assert.Equal("Demo", match.Metadata.ProductionType);
        Assert.Equal("1992", match.Metadata.ReleaseDate);
        Assert.Equal(SoftwareCategory.Demo, match.Metadata.Category);
        Assert.Equal(match.Metadata.ScreenshotUrl, match.Metadata.CoverUrl);
        Assert.Contains("screenshots/1.png", match.Metadata.ScreenshotUrl);
        Assert.Contains("title=State%20of%20the%20Art", handler.RequestUri!.Query);
    }

    [Fact]
    public async Task TwoSceneImagesUseSeparateCoverAndScreenshot()
    {
        var handler = new StubHandler("""
            {"results":[{"title":"Demo","platforms":[{"name":"Amiga AGA"}],
              "screenshots":[
                {"standard_url":"https://demozoo.org/media/first.png"},
                {"standard_url":"https://demozoo.org/media/second.png"}]}]}
            """);
        using var http = new HttpClient(handler);
        var match = Assert.Single(await new DemozooClient(http).SearchAsync("Demo"));
        Assert.EndsWith("first.png", match.Metadata.CoverUrl);
        Assert.EndsWith("second.png", match.Metadata.ScreenshotUrl);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("https://demozoo.org/productions/12345/")]
    [InlineData("https://www.demozoo.org/productions/12345/?from=search")]
    public async Task ProductionIdLoadsDetailAndPreservesSceneMetadata(string query)
    {
        var handler = new StubHandler("""
            {"id":12345,"title":"State of the Art","release_date":"1992",
             "demozoo_url":"https://demozoo.org/productions/12345/",
             "platforms":[{"name":"Amiga OCS/ECS"}],"types":[{"name":"Demo"}],
             "author_nicks":[{"releaser":{"name":"Spaceballs","is_group":true}}],
             "screenshots":[{"standard_url":"https://demozoo.org/media/demo.png"}]}
            """);
        using var http = new HttpClient(handler);
        var match = Assert.Single(await new DemozooClient(http).SearchAsync(query));
        Assert.Equal("https://demozoo.org/api/v1/productions/12345/", handler.RequestUri!.ToString());
        Assert.Equal("State of the Art", match.Metadata.Title);
        Assert.Equal("Spaceballs", match.Metadata.SceneGroup);
        Assert.Equal("Demo", match.Metadata.ProductionType);
        Assert.Equal("1992", match.Metadata.ReleaseDate);
        Assert.Equal(match.Metadata.CoverUrl, match.Metadata.ScreenshotUrl);
        Assert.Contains("ID match", match.Source);
    }

    [Fact]
    public async Task UnknownProductionIdReturnsNoMatches()
    {
        var handler = new StubHandler("{}"){ StatusCode = HttpStatusCode.NotFound };
        using var http = new HttpClient(handler);
        Assert.Empty(await new DemozooClient(http).SearchAsync("98765"));
    }

    [Fact]
    public async Task ForeignUrlIsNotUsedAsProductionId()
    {
        var handler = new StubHandler("""{"results":[]}""");
        using var http = new HttpClient(handler);
        Assert.Empty(await new DemozooClient(http).SearchAsync("https://example.org/productions/12345/"));
        Assert.Contains("?title=", handler.RequestUri!.ToString());
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            { Content = new StringContent(json) });
        }
    }
}

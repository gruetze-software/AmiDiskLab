using AmiDiskLab.Infrastructure.Metadata;
using System.Net;
using System.Net.Http;

namespace AmiDiskLab.Tests;

public class WikimediaPublisherLogoClientTests
{
    [Fact]
    public async Task ExactSoftwareCompanyUsesItsLogoClaim()
    {
        var handler = new StubHandler(uri => uri.Query.Contains("wbsearchentities", StringComparison.Ordinal)
            ? """
              {"search":[
                {"id":"Q1","label":"Acclaim Entertainment","description":"music album"},
                {"id":"Q2","label":"Acclaim Entertainment","description":"American video game publisher"}
              ]}
              """
            : """
              {"entities":{"Q2":{"claims":{"P154":[{"mainsnak":{"datavalue":{"value":"Acclaim Entertainment logo.svg"}}}]}}}}
              """);
        using var http = new HttpClient(handler);

        var logo = await new WikimediaPublisherLogoClient(http).FindLogoAsync("Acclaim Entertainment");

        Assert.Equal("https://commons.wikimedia.org/wiki/Special:Redirect/file/Acclaim%20Entertainment%20logo.svg?width=512", logo);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("ids=Q2", handler.Requests[1].Query);
        Assert.Equal("AmiDiskLab", handler.UserAgentProduct);
    }

    [Fact]
    public async Task SimilarButNonExactEntityIsNotUsed()
    {
        var handler = new StubHandler(uri => uri.Host == "commons.wikimedia.org" ? "{}" :
            "{\"search\":[{\"id\":\"Q1\",\"label\":\"Ocean Software Group\",\"description\":\"company\"}]}");
        using var http = new HttpClient(handler);

        var logo = await new WikimediaPublisherLogoClient(http).FindLogoAsync("Ocean Software");

        Assert.Null(logo);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ExactFormerCompanyNameAliasIsAccepted()
    {
        var handler = new StubHandler(uri => uri.Query.Contains("wbsearchentities", StringComparison.Ordinal)
            ? """
              {"search":[{"id":"Q3","label":"Atari SA","description":"French video game publisher",
                "match":{"type":"alias","language":"en","text":"Infogrames"}}]}
              """
            : """
              {"entities":{"Q3":{"claims":{"P154":[{"mainsnak":{"datavalue":{"value":"Infogrames logo.svg"}}}]}}}}
              """);
        using var http = new HttpClient(handler);

        var logo = await new WikimediaPublisherLogoClient(http).FindLogoAsync("Infogrames");

        Assert.Contains("Infogrames%20logo.svg", logo);
        Assert.Contains("ids=Q3", handler.Requests[1].Query);
    }

    [Fact]
    public async Task CommonsSearchIsUsedWhenWikidataHasNoLogoClaim()
    {
        var handler = new StubHandler(uri => uri.Host == "commons.wikimedia.org"
            ? """
              {"query":{"pages":[
                {"title":"File:Unrelated Logo.png","imageinfo":[{"thumburl":"https://upload.wikimedia.org/wrong.png"}]},
                {"title":"File:Psygnosis logo.svg","imageinfo":[{"thumburl":"https://upload.wikimedia.org/psygnosis.png"}]}
              ]}}
              """
            : uri.Query.Contains("wbsearchentities", StringComparison.Ordinal)
                ? "{\"search\":[{\"id\":\"Q4\",\"label\":\"Psygnosis\",\"description\":\"video game publisher\"}]}"
                : "{\"entities\":{\"Q4\":{\"claims\":{}}}}" );
        using var http = new HttpClient(handler);

        var logo = await new WikimediaPublisherLogoClient(http).FindLogoAsync("Psygnosis");

        Assert.Equal("https://upload.wikimedia.org/psygnosis.png", logo);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("commons.wikimedia.org", handler.Requests[2].Host);
    }

    [Fact]
    public async Task WikipediaArticleImageIsUsedAfterCommonsMiss()
    {
        var handler = new StubHandler(uri => uri.Host switch
        {
            "commons.wikimedia.org" => "{}",
            "en.wikipedia.org" =>
                "{\"query\":{\"pages\":[{\"thumbnail\":{\"source\":\"https://upload.wikimedia.org/psygnosis-color.png\"}}]}}",
            _ when uri.Query.Contains("wbsearchentities", StringComparison.Ordinal) =>
                "{\"search\":[{\"id\":\"Q5\",\"label\":\"Psygnosis\",\"description\":\"British video game publisher\"}]}",
            _ =>
                "{\"entities\":{\"Q5\":{\"claims\":{},\"sitelinks\":{\"enwiki\":{\"title\":\"Psygnosis\"}}}}}"
        });
        using var http = new HttpClient(handler);

        var logo = await new WikimediaPublisherLogoClient(http).FindLogoAsync("Psygnosis");

        Assert.Equal("https://upload.wikimedia.org/psygnosis-color.png", logo);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("en.wikipedia.org", handler.Requests[3].Host);
        Assert.Contains("titles=Psygnosis", handler.Requests[3].Query);
    }

    [Fact]
    public async Task PreferredFrenchWikipediaImageIsUsedForPsygnosis()
    {
        var handler = new StubHandler(uri => uri.Host switch
        {
            "commons.wikimedia.org" => "{}",
            "fr.wikipedia.org" =>
                "{\"query\":{\"pages\":[{\"title\":\"Psygnosis\",\"extract\":\"éditeur de jeux vidéo britannique\",\"thumbnail\":{\"source\":\"https://upload.wikimedia.org/psygnosis-owl-color.jpg\"}}]}}",
            _ when uri.Query.Contains("wbsearchentities", StringComparison.Ordinal) =>
                "{\"search\":[{\"id\":\"Q3410210\",\"label\":\"Psygnosis\",\"description\":\"video game publisher\"}]}",
            _ =>
                "{\"entities\":{\"Q3410210\":{\"claims\":{},\"sitelinks\":{\"frwiki\":{\"title\":\"Psygnosis\"},\"enwiki\":{\"title\":\"Psygnosis\"}}}}}"
        });
        using var http = new HttpClient(handler);

        var logo = await new WikimediaPublisherLogoClient(http).FindLogoAsync("Psygnosis", "fr");

        Assert.Equal("https://upload.wikimedia.org/psygnosis-owl-color.jpg", logo);
        Assert.Single(handler.Requests);
        Assert.Equal("fr.wikipedia.org", handler.Requests[0].Host);
        Assert.Contains("generator=search", handler.Requests[0].Query);
    }

    [Fact]
    public async Task DirectWikipediaSearchIgnoresSpacingInPublisherName()
    {
        var handler = new StubHandler(uri => uri.Host == "de.wikipedia.org"
            ? """
              {"query":{"pages":[{"title":"Team17","extract":"britischer Computerspiel-Entwickler und Publisher","thumbnail":{"source":"https://upload.wikimedia.org/team17-color.png?utm_source=de.wikipedia.org&utm_content=thumbnail_unscaled"}}]}}
              """
            : "{}");
        using var http = new HttpClient(handler);

        var logo = await new WikimediaPublisherLogoClient(http).FindLogoAsync("Team 17", "de");

        Assert.Equal("https://upload.wikimedia.org/team17-color.png", logo);
        Assert.Single(handler.Requests);
        Assert.Contains("gsrsearch=Team%2017", handler.Requests[0].Query);
    }

    [Fact]
    public async Task DirectWikipediaRejectsUnrelatedExactTitleAndUsesSoftwareCompany()
    {
        var handler = new StubHandler(uri => uri.Query.Contains("gsrsearch=Imagine%20Software",
                StringComparison.Ordinal)
            ? """
              {"query":{"pages":[{"title":"Imagine Software","extract":"britischer Computerspielentwickler und Publisher",
                "thumbnail":{"source":"https://upload.wikimedia.org/imagine-software.png"}}]}}
              """
            : """
              {"query":{"pages":[{"title":"Imagine","extract":"internationales Telekommunikationsunternehmen",
                "thumbnail":{"source":"https://upload.wikimedia.org/imagine-telecom.png"}}]}}
              """);
        using var http = new HttpClient(handler);

        var logo = await new WikimediaPublisherLogoClient(http)
            .FindLogoAsync("Imagine", "de", "Arkanoid: Revenge of Doh", "1987");

        Assert.Equal("https://upload.wikimedia.org/imagine-software.png", logo);
        Assert.Equal(2, handler.Requests.Count);
    }

    private sealed class StubHandler(Func<Uri, string> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public string? UserAgentProduct { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            UserAgentProduct = request.Headers.UserAgent.FirstOrDefault()?.Product?.Name;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(request.RequestUri!))
            });
        }
    }
}

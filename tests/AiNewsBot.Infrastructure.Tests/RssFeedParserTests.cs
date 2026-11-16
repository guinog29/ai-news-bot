using System.Text;
using System.Xml;
using AiNewsBot.Core;
using AiNewsBot.Infrastructure.Sources;

namespace AiNewsBot.Infrastructure.Tests;

public class RssFeedParserTests
{
    [Fact]
    public void Parses_rss_items_with_guid_url_fallback_and_cleaned_titles()
    {
        using var stream = TestData.OpenFixture("sample-rss.xml");

        var items = RssFeedParser.Parse(stream, TestData.Rss());

        Assert.Equal(3, items.Count);

        Assert.Equal("First post with messy whitespace", items[0].Title);
        Assert.Equal("guid:urn:example:post:1", items[0].Id);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 8, 31, 2, TimeSpan.Zero), items[0].PublishedAt);
        Assert.Equal("Example Blog", items[0].SourceName);
        Assert.Equal("official", items[0].ChannelKey);

        Assert.StartsWith("url:", items[1].Id, StringComparison.Ordinal);
        Assert.Equal(new Uri("https://blog.example.com/relative/path"), items[2].Url);
        Assert.Null(items[2].PublishedAt);
    }

    [Fact]
    public void Ignores_entries_without_a_title_or_with_non_http_links()
    {
        using var stream = TestData.OpenFixture("sample-rss.xml");

        var items = RssFeedParser.Parse(stream, TestData.Rss());

        Assert.DoesNotContain(items, i => i.Title.Contains("Unsafe", StringComparison.Ordinal));
        Assert.DoesNotContain(items, i => i.Url.AbsoluteUri.Contains("untitled", StringComparison.Ordinal));
    }

    [Fact]
    public void Falls_back_to_a_url_hash_when_there_is_no_guid()
    {
        const string xml = """
            <rss version="2.0"><channel><title>t</title>
              <item><title>No guid</title><link>https://blog.example.com/a?utm_source=x</link></item>
              <item><title>No guid again</title><link>https://BLOG.example.com/a/</link></item>
            </channel></rss>
            """;

        var items = RssFeedParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)), TestData.Rss());

        Assert.All(items, i => Assert.StartsWith("url:", i.Id, StringComparison.Ordinal));
        Assert.Equal(items[0].Id, items[1].Id);
    }

    [Fact]
    public void Parses_atom_and_prefers_the_alternate_link()
    {
        using var stream = TestData.OpenFixture("sample-atom.xml");

        var items = RssFeedParser.Parse(stream, TestData.Rss("https://atom.example.com/feed"));

        Assert.Equal(2, items.Count);
        Assert.Equal(new Uri("https://atom.example.com/entry-1"), items[0].Url);
        Assert.Equal("guid:tag:example.com,2026:entry-1", items[0].Id);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero), items[0].PublishedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero), items[1].PublishedAt);
    }

    [Fact]
    public void Rejects_documents_with_a_doctype()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY x "boom">]>
            <rss version="2.0"><channel><title>t</title></channel></rss>
            """;

        Assert.Throws<XmlException>(() => RssFeedParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)), TestData.Rss()));
    }

    [Fact]
    public void Rejects_documents_that_are_not_feeds()
    {
        var html = new MemoryStream(Encoding.UTF8.GetBytes("<html><body>Not a feed</body></html>"));

        Assert.ThrowsAny<Exception>(() => RssFeedParser.Parse(html, TestData.Rss()));
    }

    [Fact]
    public async Task Source_fetches_over_http_and_parses()
    {
        using var handler = new StubHandler(_ => StubHandler.Respond(File.ReadAllText(TestData.FixturePath("sample-rss.xml")), "application/rss+xml"));
        using var http = new HttpClient(handler);
        var source = new RssFeedSource(http, TestData.Rss());

        var items = await source.FetchAsync(CancellationToken.None);

        Assert.Equal(3, items.Count);
        Assert.Equal("Example Blog", source.Name);
    }

    [Fact]
    public async Task Source_surfaces_http_errors()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        using var http = new HttpClient(handler);
        var source = new RssFeedSource(http, TestData.Rss());

        await Assert.ThrowsAsync<HttpRequestException>(() => source.FetchAsync(CancellationToken.None));
    }
}

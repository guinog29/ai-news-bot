using AiNewsBot.Infrastructure.Sources;

namespace AiNewsBot.Infrastructure.Tests;

public class AnthropicNewsParserTests
{
    private static IReadOnlyList<AiNewsBot.Core.FeedItem> ParseFixture() =>
        AnthropicNewsParser.Parse(File.ReadAllText(TestData.FixturePath("anthropic-news.html")), TestData.Anthropic());

    [Fact]
    public void Finds_article_cards_in_both_layouts_and_ignores_everything_else()
    {
        var items = ParseFixture();

        Assert.Equal(
            ["big-launch", "expanding-partnership", "no-date-article", "full-month-name", "late-date"],
            items.Select(i => i.Url.AbsolutePath.Split('/')[^1]));
    }

    [Fact]
    public void Builds_absolute_urls_titles_and_source_fields()
    {
        var first = ParseFixture()[0];

        Assert.Equal(new Uri("https://www.anthropic.com/news/big-launch"), first.Url);
        Assert.Equal("Big launch of a new model", first.Title);
        Assert.Equal("Anthropic", first.SourceName);
        Assert.Equal("official", first.ChannelKey);
        Assert.StartsWith("url:", first.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_dates_from_the_enclosing_article_or_list_row()
    {
        var byName = ParseFixture().ToDictionary(i => i.Url.AbsolutePath.Split('/')[^1]);

        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), byName["big-launch"].PublishedAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero), byName["expanding-partnership"].PublishedAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero), byName["full-month-name"].PublishedAt);
        Assert.Null(byName["no-date-article"].PublishedAt);
    }

    [Fact]
    public void Keeps_a_date_found_on_a_later_occurrence_of_the_same_article()
    {
        var late = ParseFixture().Single(i => i.Url.AbsolutePath.EndsWith("late-date", StringComparison.Ordinal));

        Assert.Equal(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero), late.PublishedAt);
    }

    [Fact]
    public void Ids_are_stable_across_parses()
    {
        Assert.Equal(ParseFixture().Select(i => i.Id), ParseFixture().Select(i => i.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body><p>Site redesigned, no cards here.</p></body></html>")]
    public void Returns_nothing_when_the_markup_changes_so_the_job_can_flag_a_failure(string html)
    {
        Assert.Empty(AnthropicNewsParser.Parse(html, TestData.Anthropic()));
    }

    [Fact]
    public async Task Source_fetches_the_page_and_parses_it()
    {
        using var handler = new StubHandler(_ => StubHandler.Respond(File.ReadAllText(TestData.FixturePath("anthropic-news.html")), "text/html"));
        using var http = new HttpClient(handler);

        var items = await new AnthropicNewsSource(http, TestData.Anthropic()).FetchAsync(CancellationToken.None);

        Assert.Equal(5, items.Count);
    }
}

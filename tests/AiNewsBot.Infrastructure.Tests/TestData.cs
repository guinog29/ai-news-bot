using AiNewsBot.Core;

namespace AiNewsBot.Infrastructure.Tests;

internal static class TestData
{
    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static FileStream OpenFixture(string name) => File.OpenRead(FixturePath(name));

    public static FeedSourceDefinition Rss(string url = "https://blog.example.com/feed.xml") =>
        new("Example Blog", "official", SourceKind.Rss, new Uri(url));

    public static FeedSourceDefinition Anthropic() =>
        new("Anthropic", "official", SourceKind.AnthropicNews, new Uri("https://www.anthropic.com/news"));
}

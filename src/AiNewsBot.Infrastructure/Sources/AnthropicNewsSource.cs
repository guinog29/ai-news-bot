using AiNewsBot.Core;

namespace AiNewsBot.Infrastructure.Sources;

/// <summary>Collects Anthropic's news by reading its HTML page (the site has no feed).</summary>
public sealed class AnthropicNewsSource(HttpClient http, FeedSourceDefinition definition) : IFeedSource
{
    /// <inheritdoc />
    public string Name => definition.Name;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken cancellationToken)
    {
        var html = await http.GetStringAsync(definition.Url, cancellationToken);
        return AnthropicNewsParser.Parse(html, definition);
    }
}

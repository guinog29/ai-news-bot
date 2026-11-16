using AiNewsBot.Core;

namespace AiNewsBot.Infrastructure.Sources;

/// <summary>Reads an RSS or Atom feed over HTTP.</summary>
public sealed class RssFeedSource(HttpClient http, FeedSourceDefinition definition) : IFeedSource
{
    /// <inheritdoc />
    public string Name => definition.Name;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken cancellationToken)
    {
        await using var stream = await http.GetStreamAsync(definition.Url, cancellationToken);
        return RssFeedParser.Parse(stream, definition);
    }
}

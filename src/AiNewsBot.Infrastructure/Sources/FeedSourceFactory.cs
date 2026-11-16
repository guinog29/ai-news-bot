using AiNewsBot.Core;

namespace AiNewsBot.Infrastructure.Sources;

/// <summary>Builds the <see cref="IFeedSource"/> for a configured source.</summary>
public static class FeedSourceFactory
{
    /// <summary>Creates the source implementation matching <see cref="FeedSourceDefinition.Kind"/>.</summary>
    public static IFeedSource Create(FeedSourceDefinition definition, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kind switch
        {
            SourceKind.Rss => new RssFeedSource(http, definition),
            SourceKind.AnthropicNews => new AnthropicNewsSource(http, definition),
            _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.Kind, "Unknown source kind."),
        };
    }
}

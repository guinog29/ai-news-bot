namespace AiNewsBot.Core;

/// <summary>How a source is read.</summary>
public enum SourceKind
{
    /// <summary>RSS or Atom feed.</summary>
    Rss,

    /// <summary>Anthropic's news page, which has no feed (HTML collector, D12).</summary>
    AnthropicNews,
}

/// <summary>A configured source: where to read it and which channel it posts to.</summary>
/// <param name="Name">Unique display name; also the key of the source in the state file.</param>
/// <param name="Channel">Logical channel key (see <see cref="ChannelKeys"/>).</param>
/// <param name="Kind">How the source is read.</param>
/// <param name="Url">Feed or page URL.</param>
public sealed record FeedSourceDefinition(string Name, string Channel, SourceKind Kind, Uri Url);

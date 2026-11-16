namespace AiNewsBot.Core;

/// <summary>A single news item produced by a source.</summary>
/// <param name="Id">Stable identifier used for deduplication (see <see cref="ItemIdentity"/>).</param>
/// <param name="SourceName">Display name of the source, shown in the embed footer.</param>
/// <param name="ChannelKey">Logical channel key the item is posted to (D18).</param>
/// <param name="Title">Item title.</param>
/// <param name="Url">Link to the original article.</param>
/// <param name="PublishedAt">Publication time, when the source provides one.</param>
public sealed record FeedItem(
    string Id,
    string SourceName,
    string ChannelKey,
    string Title,
    Uri Url,
    DateTimeOffset? PublishedAt);

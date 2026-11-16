namespace AiNewsBot.Core;

/// <summary>A place news items are read from.</summary>
public interface IFeedSource
{
    /// <summary>Unique display name of the source.</summary>
    string Name { get; }

    /// <summary>Fetches the items currently published by the source, in any order.</summary>
    /// <exception cref="Exception">Any failure to fetch or parse; the job counts it as a source failure.</exception>
    Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken cancellationToken);
}

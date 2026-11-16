namespace AiNewsBot.Core;

/// <summary>Delivers items and alerts to the chat platform.</summary>
public interface INewsPublisher
{
    /// <summary>Posts one item to the channel named by <see cref="FeedItem.ChannelKey"/>.</summary>
    Task PublishAsync(FeedItem item, CancellationToken cancellationToken);

    /// <summary>Posts a plain-text alert to the status channel.</summary>
    Task PublishStatusAsync(string message, CancellationToken cancellationToken);
}

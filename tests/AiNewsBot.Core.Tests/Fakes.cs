using Microsoft.Extensions.Logging.Abstractions;

namespace AiNewsBot.Core.Tests;

internal sealed class FakeSource(string name, string channel = "official") : IFeedSource
{
    public string Name => name;

    public string Channel => channel;

    public Queue<Func<IReadOnlyList<FeedItem>>> Responses { get; } = new();

    public Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Responses.Dequeue()());

    public FakeSource Returns(params FeedItem[] items)
    {
        Responses.Enqueue(() => items);
        return this;
    }

    public FakeSource Throws(string message)
    {
        Responses.Enqueue(() => throw new InvalidOperationException(message));
        return this;
    }

    public FeedItem Item(string id, int day) =>
        new(id, name, channel, $"Title {id}", new Uri($"https://example.com/{name}/{id}"), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).AddDays(day));
}

internal sealed class FakePublisher : INewsPublisher
{
    public List<string> Published { get; } = [];

    public List<string> Statuses { get; } = [];

    public HashSet<string> FailOn { get; } = [];

    public bool FailStatus { get; set; }

    public Task PublishAsync(FeedItem item, CancellationToken cancellationToken)
    {
        if (FailOn.Contains(item.Id))
        {
            throw new InvalidOperationException("discord is down");
        }

        Published.Add(item.Id);
        return Task.CompletedTask;
    }

    public Task PublishStatusAsync(string message, CancellationToken cancellationToken)
    {
        if (FailStatus)
        {
            throw new InvalidOperationException("status channel is down");
        }

        Statuses.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class FakeStore(BotState? initial = null) : IStateStore
{
    public BotState? State { get; private set; } = initial;

    public int Saves { get; private set; }

    public Task<BotState?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(State);

    public Task SaveAsync(BotState state, CancellationToken cancellationToken)
    {
        State = state;
        Saves++;
        return Task.CompletedTask;
    }
}

internal static class Jobs
{
    public static readonly BotOptions Options = new()
    {
        ConsiderLatest = 10,
        MaxSeenPerSource = 20,
        MaxPostsPerSourcePerRun = 3,
        FailureAlertThreshold = 2,
    };

    public static NewsJob Create(IReadOnlyList<IFeedSource> sources, FakeStore store, FakePublisher publisher) =>
        new(sources, store, publisher, Options, TimeProvider.System, NullLogger<NewsJob>.Instance);
}

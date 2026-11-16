namespace AiNewsBot.Core;

/// <summary>Outcome of comparing a source's items against its state.</summary>
/// <param name="Considered">The newest items looked at, newest first (deduplicated).</param>
/// <param name="ToPublish">Items to post, oldest first.</param>
/// <param name="Skipped">New items dropped by the per-run cap (D8), oldest first.</param>
/// <param name="IsSeed">True when the source has no state yet: nothing is posted, items are only recorded (D7).</param>
public sealed record Selection(
    IReadOnlyList<FeedItem> Considered,
    IReadOnlyList<FeedItem> ToPublish,
    IReadOnlyList<FeedItem> Skipped,
    bool IsSeed);

/// <summary>Decides which items are new (D5, D6, D7, D8). Pure: no I/O.</summary>
public static class NewItemSelector
{
    /// <summary>Selects what to post for one source.</summary>
    /// <param name="fetched">Everything the source returned.</param>
    /// <param name="state">The source's state, or <see langword="null"/> if it has none.</param>
    /// <param name="options">Limits.</param>
    public static Selection Select(IEnumerable<FeedItem> fetched, SourceState? state, BotOptions options)
    {
        ArgumentNullException.ThrowIfNull(fetched);
        ArgumentNullException.ThrowIfNull(options);

        var considered = fetched
            .DistinctBy(i => i.Id, StringComparer.Ordinal)
            .OrderByDescending(SortKey)
            .Take(options.ConsiderLatest)
            .ToList();

        if (state is not { Seeded: true })
        {
            return new Selection(considered, [], [], IsSeed: true);
        }

        var seen = state.SeenIds.ToHashSet(StringComparer.Ordinal);
        var fresh = considered.Where(i => !seen.Contains(i.Id)).ToList();
        fresh.Reverse(); // oldest first; the sort below is stable, so ties keep this order
        fresh = fresh.OrderBy(SortKey).ToList();

        var dropped = Math.Max(0, fresh.Count - options.MaxPostsPerSourcePerRun);
        return new Selection(considered, fresh.Skip(dropped).ToList(), fresh.Take(dropped).ToList(), IsSeed: false);
    }

    // Items without a date are treated as the newest.
    private static DateTimeOffset SortKey(FeedItem item) => item.PublishedAt ?? DateTimeOffset.MaxValue;
}

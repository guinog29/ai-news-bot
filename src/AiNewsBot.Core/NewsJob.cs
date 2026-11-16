using Microsoft.Extensions.Logging;

namespace AiNewsBot.Core;

/// <summary>What happened to one source in a run.</summary>
/// <param name="Source">Source name.</param>
/// <param name="Succeeded">False if the fetch or any post failed.</param>
/// <param name="Published">Items posted.</param>
/// <param name="Dropped">New items dropped by the per-run cap.</param>
/// <param name="Seeded">True if the source was only seeded this run (nothing posted).</param>
/// <param name="Error">Failure reason, when it failed.</param>
public sealed record SourceResult(string Source, bool Succeeded, int Published, int Dropped, bool Seeded, string? Error);

/// <summary>Outcome of a whole run.</summary>
/// <param name="Sources">One entry per configured source.</param>
public sealed record JobResult(IReadOnlyList<SourceResult> Sources)
{
    /// <summary>Total items posted.</summary>
    public int Published => Sources.Sum(s => s.Published);

    /// <summary>Number of sources that failed.</summary>
    public int Failed => Sources.Count(s => !s.Succeeded);

    /// <summary>True when there were sources and none of them worked: the run is a critical failure (D11).</summary>
    public bool AllFailed => Sources.Count > 0 && Failed == Sources.Count;
}

/// <summary>One run of the bot: read sources, post what is new, update state, alert on repeated failures.</summary>
public sealed class NewsJob
{
    private readonly IReadOnlyList<IFeedSource> _sources;
    private readonly IStateStore _stateStore;
    private readonly INewsPublisher _publisher;
    private readonly BotOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<NewsJob> _logger;

    /// <summary>Creates the job.</summary>
    public NewsJob(
        IReadOnlyList<IFeedSource> sources,
        IStateStore stateStore,
        INewsPublisher publisher,
        BotOptions options,
        TimeProvider time,
        ILogger<NewsJob> logger)
    {
        _sources = sources;
        _stateStore = stateStore;
        _publisher = publisher;
        _options = options.Validate();
        _time = time;
        _logger = logger;
    }

    /// <summary>Runs once. Failures of a single source are recorded and do not stop the run.</summary>
    /// <exception cref="Exception">State could not be loaded or saved; the caller must fail loudly (D11).</exception>
    public async Task<JobResult> RunAsync(CancellationToken cancellationToken)
    {
        var state = await _stateStore.LoadAsync(cancellationToken);
        if (state is null)
        {
            _logger.LogWarning("No previous state found: every source will be seeded without posting (D7).");
            state = new BotState();
        }

        var names = _sources.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in state.Sources.Keys.Where(k => !names.Contains(k)).ToList())
        {
            state.Sources.Remove(removed);
        }

        var results = new List<SourceResult>();
        foreach (var source in _sources)
        {
            results.Add(await ProcessAsync(source, state, cancellationToken));
        }

        await _stateStore.SaveAsync(state, cancellationToken);
        return new JobResult(results);
    }

    private async Task<SourceResult> ProcessAsync(IFeedSource source, BotState state, CancellationToken ct)
    {
        if (!state.Sources.TryGetValue(source.Name, out var sourceState))
        {
            sourceState = new SourceState();
            state.Sources[source.Name] = sourceState;
        }

        IReadOnlyList<FeedItem> fetched;
        try
        {
            fetched = await source.FetchAsync(ct);
            if (fetched.Count == 0)
            {
                // A feed or scraper that suddenly finds nothing is almost always broken (D12).
                throw new InvalidOperationException("The source returned zero items.");
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return await FailAsync(source, sourceState, $"Fetch failed: {ex.Message}", published: 0, dropped: 0, ct);
        }

        var selection = NewItemSelector.Select(fetched, sourceState, _options);
        var wasAlerting = sourceState.ConsecutiveFailures >= _options.FailureAlertThreshold;

        if (selection.IsSeed)
        {
            RememberIds(sourceState, selection.Considered.Reverse().Select(i => i.Id));
            sourceState.Seeded = true;
            MarkSuccess(sourceState);
            _logger.LogInformation("{Source}: seeded with {Count} items, nothing posted.", source.Name, selection.Considered.Count);
            await RecoverAsync(source, wasAlerting, ct);
            return new SourceResult(source.Name, true, 0, 0, Seeded: true, null);
        }

        if (selection.Skipped.Count > 0)
        {
            _logger.LogWarning(
                "{Source}: {Count} new items exceed the limit of {Limit} per run and were dropped.",
                source.Name,
                selection.Skipped.Count,
                _options.MaxPostsPerSourcePerRun);
            RememberIds(sourceState, selection.Skipped.Select(i => i.Id));
        }

        var published = 0;
        foreach (var item in selection.ToPublish)
        {
            try
            {
                await _publisher.PublishAsync(item, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Stop here so order is kept; the unposted items stay unseen and are retried next run (D5).
                return await FailAsync(source, sourceState, $"Posting failed: {ex.Message}", published, selection.Skipped.Count, ct);
            }

            RememberIds(sourceState, [item.Id]);
            published++;
        }

        MarkSuccess(sourceState);
        _logger.LogInformation("{Source}: {Count} items posted.", source.Name, published);
        await RecoverAsync(source, wasAlerting, ct);
        return new SourceResult(source.Name, true, published, selection.Skipped.Count, Seeded: false, null);
    }

    private async Task<SourceResult> FailAsync(
        IFeedSource source, SourceState sourceState, string error, int published, int dropped, CancellationToken ct)
    {
        sourceState.ConsecutiveFailures++;
        _logger.LogWarning(
            "{Source}: {Error} ({Failures} failed runs in a row)",
            source.Name,
            error,
            sourceState.ConsecutiveFailures);

        if (sourceState.ConsecutiveFailures == _options.FailureAlertThreshold)
        {
            await TryStatusAsync(
                $"⚠️ **{source.Name}** has failed {sourceState.ConsecutiveFailures} runs in a row.\nLast error: {Truncate(error, 500)}",
                ct);
        }

        return new SourceResult(source.Name, false, published, dropped, Seeded: false, error);
    }

    private async Task RecoverAsync(IFeedSource source, bool wasAlerting, CancellationToken ct)
    {
        if (wasAlerting)
        {
            await TryStatusAsync($"✅ **{source.Name}** is working again.", ct);
        }
    }

    private async Task TryStatusAsync(string message, CancellationToken ct)
    {
        try
        {
            await _publisher.PublishStatusAsync(message, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // A broken status channel must not break the run; the workflow's own failure email is the fallback (D11).
            _logger.LogError("Could not post to the status channel: {Error}", ex.Message);
        }
    }

    private void MarkSuccess(SourceState sourceState)
    {
        sourceState.ConsecutiveFailures = 0;
        sourceState.LastSuccessAt = _time.GetUtcNow();
    }

    /// <summary>Adds identifiers (given oldest to newest) at the front of the list and trims the oldest (D6).</summary>
    private void RememberIds(SourceState sourceState, IEnumerable<string> idsOldestFirst)
    {
        foreach (var id in idsOldestFirst)
        {
            if (!sourceState.SeenIds.Contains(id, StringComparer.Ordinal))
            {
                sourceState.SeenIds.Insert(0, id);
            }
        }

        if (sourceState.SeenIds.Count > _options.MaxSeenPerSource)
        {
            sourceState.SeenIds.RemoveRange(_options.MaxSeenPerSource, sourceState.SeenIds.Count - _options.MaxSeenPerSource);
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}

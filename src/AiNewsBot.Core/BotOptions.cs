namespace AiNewsBot.Core;

/// <summary>Tunable limits. Defaults are the initial values for the open questions in D6, D8 and D11.</summary>
public sealed record BotOptions
{
    /// <summary>Only the newest N items of a source are considered per run. Keeps huge feeds from re-surfacing old items.</summary>
    public int ConsiderLatest { get; init; } = 50;

    /// <summary>How many item identifiers are remembered per source (D6).</summary>
    public int MaxSeenPerSource { get; init; } = 300;

    /// <summary>Most items posted per source in one run; the excess is dropped (D8).</summary>
    public int MaxPostsPerSourcePerRun { get; init; } = 5;

    /// <summary>Failed runs in a row before an alert goes to the status channel (D11).</summary>
    public int FailureAlertThreshold { get; init; } = 4;

    /// <summary>Returns this instance after checking that the values are usable.</summary>
    /// <exception cref="ArgumentException">A value is out of range.</exception>
    public BotOptions Validate()
    {
        Require(ConsiderLatest > 0, nameof(ConsiderLatest));
        Require(MaxSeenPerSource >= ConsiderLatest, $"{nameof(MaxSeenPerSource)} (must be >= {nameof(ConsiderLatest)})");
        Require(MaxPostsPerSourcePerRun > 0, nameof(MaxPostsPerSourcePerRun));
        Require(FailureAlertThreshold > 0, nameof(FailureAlertThreshold));
        return this;
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new ArgumentException($"Invalid option: {name}.");
        }
    }
}

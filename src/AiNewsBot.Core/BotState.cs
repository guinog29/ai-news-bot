using System.Text.Json.Serialization;

namespace AiNewsBot.Core;

/// <summary>Everything the bot remembers between runs. Serialized as the JSON on the state branch (D15).</summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class BotState
{
    /// <summary>Schema version of the state file.</summary>
    public int Version { get; set; } = 1;

    /// <summary>State per source, keyed by source name. Sorted so the committed JSON diffs cleanly.</summary>
    public SortedDictionary<string, SourceState> Sources { get; } = new(StringComparer.Ordinal);
}

/// <summary>What the bot remembers about one source.</summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class SourceState
{
    /// <summary>Whether the source was already seeded. Until then nothing is posted (D7).</summary>
    public bool Seeded { get; set; }

    /// <summary>Runs in a row in which the source failed. Reset on success.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>When the source last succeeded.</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>Identifiers of items already handled, newest first, capped (D6).</summary>
    public List<string> SeenIds { get; } = [];
}

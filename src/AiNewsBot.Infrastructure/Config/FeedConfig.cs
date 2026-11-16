using System.Text.Json;
using System.Text.Json.Serialization;
using AiNewsBot.Core;

namespace AiNewsBot.Infrastructure.Config;

/// <summary>The versioned configuration: which sources to read and the tunable limits.</summary>
/// <param name="Sources">Configured sources.</param>
/// <param name="Options">Limits (defaults apply to omitted values).</param>
public sealed record FeedConfig(IReadOnlyList<FeedSourceDefinition> Sources, BotOptions Options)
{
    /// <summary>Distinct channel keys used by the sources.</summary>
    public IReadOnlyList<string> ChannelKeys => Sources.Select(s => s.Channel).Distinct(StringComparer.Ordinal).ToList();
}

/// <summary>Reads and validates the feed configuration file. It contains no secrets (D9, D18).</summary>
public static class FeedConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Loads the configuration from a file.</summary>
    /// <exception cref="InvalidOperationException">The file is missing or invalid.</exception>
    public static FeedConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Feed configuration not found: {path}");
        }

        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parses and validates configuration JSON.</summary>
    /// <exception cref="InvalidOperationException">The JSON is invalid or breaks a rule.</exception>
    public static FeedConfig Parse(string json)
    {
        FileModel? file;
        try
        {
            file = JsonSerializer.Deserialize<FileModel>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid feed configuration: {ex.Message}", ex);
        }

        if (file?.Sources is not { Count: > 0 })
        {
            throw new InvalidOperationException("Invalid feed configuration: at least one source is required.");
        }

        var sources = file.Sources.Select(ToDefinition).ToList();
        var duplicate = sources.GroupBy(s => s.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Invalid feed configuration: duplicate source name '{duplicate.Key}'.");
        }

        try
        {
            return new FeedConfig(sources, (file.Options ?? new BotOptions()).Validate());
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Invalid feed configuration: {ex.Message}", ex);
        }
    }

    private static FeedSourceDefinition ToDefinition(SourceModel source)
    {
        if (string.IsNullOrWhiteSpace(source.Name))
        {
            throw new InvalidOperationException("Invalid feed configuration: every source needs a name.");
        }

        if (!ChannelKeys.IsValid(source.Channel))
        {
            throw new InvalidOperationException($"Invalid feed configuration: source '{source.Name}' has an invalid channel key.");
        }

        if (source.Channel == ChannelKeys.Status)
        {
            throw new InvalidOperationException($"Invalid feed configuration: source '{source.Name}' cannot post to the '{ChannelKeys.Status}' channel.");
        }

        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"Invalid feed configuration: source '{source.Name}' needs an absolute https url.");
        }

        var kind = source.Type switch
        {
            "rss" => SourceKind.Rss,
            "anthropic-news" => SourceKind.AnthropicNews,
            _ => throw new InvalidOperationException($"Invalid feed configuration: source '{source.Name}' has unknown type '{source.Type}'."),
        };

        return new FeedSourceDefinition(source.Name, source.Channel!, kind, url);
    }

    private sealed class FileModel
    {
        public List<SourceModel>? Sources { get; set; }

        public BotOptions? Options { get; set; }
    }

    private sealed class SourceModel
    {
        public string? Name { get; set; }

        public string? Channel { get; set; }

        public string? Type { get; set; }

        public string? Url { get; set; }
    }
}

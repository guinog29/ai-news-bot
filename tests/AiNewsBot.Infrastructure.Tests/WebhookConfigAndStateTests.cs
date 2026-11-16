using AiNewsBot.Core;
using AiNewsBot.Infrastructure.Config;
using AiNewsBot.Infrastructure.Discord;
using AiNewsBot.Infrastructure.Sources;
using AiNewsBot.Infrastructure.State;

namespace AiNewsBot.Infrastructure.Tests;

public sealed class WebhookConfigTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] pairs) =>
        name => pairs.Where(p => p.Name == name).Select(p => p.Value).FirstOrDefault();

    [Fact]
    public void Resolves_required_and_status_webhooks()
    {
        var result = DiscordWebhookConfig.Resolve(
            ["official"],
            Env(("DISCORD_WEBHOOK_OFFICIAL", "https://discord.com/api/webhooks/1/a"), ("DISCORD_WEBHOOK_STATUS", "https://discord.com/api/webhooks/2/b")));

        Assert.Equal(["official", "status"], result.Keys.Order());
    }

    [Fact]
    public void Status_is_optional()
    {
        var result = DiscordWebhookConfig.Resolve(["official"], Env(("DISCORD_WEBHOOK_OFFICIAL", "https://discord.com/api/webhooks/1/a")));

        Assert.Equal(["official"], result.Keys);
    }

    [Fact]
    public void A_missing_required_webhook_names_the_variable()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DiscordWebhookConfig.Resolve(["official", "summary"], Env()));

        Assert.Contains("DISCORD_WEBHOOK_OFFICIAL is not set", error.Message, StringComparison.Ordinal);
        Assert.Contains("DISCORD_WEBHOOK_SUMMARY is not set", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_value_is_reported_without_echoing_it()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => DiscordWebhookConfig.Resolve(["official"], Env(("DISCORD_WEBHOOK_OFFICIAL", "http://leaky.example/secret-token"))));

        Assert.Contains("DISCORD_WEBHOOK_OFFICIAL", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", error.Message, StringComparison.Ordinal);
    }
}

public class FeedConfigLoaderTests
{
    private const string Valid = """
        {
          // comments are allowed
          "sources": [
            { "name": "A", "channel": "official", "type": "rss", "url": "https://a.example.com/feed" },
            { "name": "B", "channel": "summary", "type": "anthropic-news", "url": "https://b.example.com/news" },
          ],
          "options": { "maxPostsPerSourcePerRun": 2 }
        }
        """;

    [Fact]
    public void Parses_sources_and_merges_options_with_defaults()
    {
        var config = FeedConfigLoader.Parse(Valid);

        Assert.Equal(2, config.Sources.Count);
        Assert.Equal(SourceKind.AnthropicNews, config.Sources[1].Kind);
        Assert.Equal(2, config.Options.MaxPostsPerSourcePerRun);
        Assert.Equal(new BotOptions().ConsiderLatest, config.Options.ConsiderLatest);
        Assert.Equal(["official", "summary"], config.ChannelKeys);
    }

    [Theory]
    [InlineData("{ \"sources\": [] }")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{ \"sources\": [ { \"name\": \"A\", \"channel\": \"Bad-Key\", \"type\": \"rss\", \"url\": \"https://a.example.com\" } ] }")]
    [InlineData("{ \"sources\": [ { \"name\": \"A\", \"channel\": \"status\", \"type\": \"rss\", \"url\": \"https://a.example.com\" } ] }")]
    [InlineData("{ \"sources\": [ { \"name\": \"A\", \"channel\": \"official\", \"type\": \"ftp\", \"url\": \"https://a.example.com\" } ] }")]
    [InlineData("{ \"sources\": [ { \"name\": \"A\", \"channel\": \"official\", \"type\": \"rss\", \"url\": \"http://a.example.com\" } ] }")]
    [InlineData("{ \"sources\": [ { \"name\": \"\", \"channel\": \"official\", \"type\": \"rss\", \"url\": \"https://a.example.com\" } ] }")]
    [InlineData("{ \"sources\": [ { \"name\": \"A\", \"channel\": \"official\", \"type\": \"rss\", \"url\": \"https://a.example.com\", \"typo\": 1 } ] }")]
    [InlineData("{ \"sources\": [ { \"name\": \"A\", \"channel\": \"official\", \"type\": \"rss\", \"url\": \"https://a.example.com\" } ], \"options\": { \"maxPostsPerSourcePerRun\": 0 } }")]
    public void Rejects_invalid_configuration(string json) =>
        Assert.Throws<InvalidOperationException>(() => FeedConfigLoader.Parse(json));

    [Fact]
    public void Rejects_duplicate_source_names()
    {
        const string json = """
            { "sources": [
              { "name": "A", "channel": "official", "type": "rss", "url": "https://a.example.com/1" },
              { "name": "A", "channel": "official", "type": "rss", "url": "https://a.example.com/2" } ] }
            """;

        var error = Assert.Throws<InvalidOperationException>(() => FeedConfigLoader.Parse(json));

        Assert.Contains("duplicate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_file_is_reported()
    {
        Assert.Throws<InvalidOperationException>(() => FeedConfigLoader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "feeds.json")));
    }

    [Fact]
    public void The_shipped_config_is_valid_and_builds_every_source()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "config", "feeds.json")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        var config = FeedConfigLoader.Load(Path.Combine(root, "config", "feeds.json"));
        using var http = new HttpClient();

        var sources = config.Sources.Select(s => FeedSourceFactory.Create(s, http)).ToList();

        Assert.Equal(config.Sources.Count, sources.Count);
        Assert.Contains(config.Sources, s => s.Kind == SourceKind.AnthropicNews);
    }
}

public sealed class JsonFileStateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ainewsbot-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string StatePath => Path.Combine(_directory, "nested", "state.json");

    [Fact]
    public async Task A_missing_file_means_no_state()
    {
        Assert.Null(await new JsonFileStateStore(StatePath).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task State_survives_a_round_trip_and_creates_missing_folders()
    {
        var state = new BotState();
        var source = new SourceState { Seeded = true, ConsecutiveFailures = 2, LastSuccessAt = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero) };
        source.SeenIds.AddRange(["guid:2", "guid:1"]);
        state.Sources["Zeta"] = source;
        state.Sources["Alpha"] = new SourceState();
        var store = new JsonFileStateStore(StatePath);

        await store.SaveAsync(state, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(["Alpha", "Zeta"], loaded.Sources.Keys);
        Assert.True(loaded.Sources["Zeta"].Seeded);
        Assert.Equal(2, loaded.Sources["Zeta"].ConsecutiveFailures);
        Assert.Equal(["guid:2", "guid:1"], loaded.Sources["Zeta"].SeenIds);
        Assert.Equal(source.LastSuccessAt, loaded.Sources["Zeta"].LastSuccessAt);
    }

    [Fact]
    public async Task The_file_is_stable_lf_json_without_a_leftover_temp_file()
    {
        var store = new JsonFileStateStore(StatePath);
        var state = new BotState();
        state.Sources["B"] = new SourceState();
        state.Sources["A"] = new SourceState();

        await store.SaveAsync(state, CancellationToken.None);
        await store.SaveAsync(state, CancellationToken.None);
        var text = await File.ReadAllTextAsync(StatePath);

        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("\"A\"", StringComparison.Ordinal) < text.IndexOf("\"B\"", StringComparison.Ordinal));
        Assert.False(File.Exists(StatePath + ".tmp"));
    }

    [Fact]
    public async Task A_corrupt_file_fails_loudly_instead_of_silently_reseeding()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        await File.WriteAllTextAsync(StatePath, "{ not json");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new JsonFileStateStore(StatePath).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_version_is_rejected()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        await File.WriteAllTextAsync(StatePath, "{ \"version\": 99, \"sources\": {} }");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new JsonFileStateStore(StatePath).LoadAsync(CancellationToken.None));
    }
}

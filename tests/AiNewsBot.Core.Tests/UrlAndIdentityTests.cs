namespace AiNewsBot.Core.Tests;

public class UrlAndIdentityTests
{
    [Theory]
    [InlineData("HTTPS://Example.com/Post/", "https://example.com/Post")]
    [InlineData("https://example.com/post#section", "https://example.com/post")]
    [InlineData("https://example.com:443/post", "https://example.com/post")]
    [InlineData("https://example.com:8443/post", "https://example.com:8443/post")]
    [InlineData("https://example.com/post?utm_source=x&b=2&a=1&fbclid=z", "https://example.com/post?a=1&b=2")]
    [InlineData("https://example.com/", "https://example.com/")]
    public void Normalize_reduces_equivalent_urls(string input, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(new Uri(input)));

    [Fact]
    public void Create_prefers_the_guid()
    {
        var id = ItemIdentity.Create("  abc-123 ", new Uri("https://example.com/a"));

        Assert.Equal("guid:abc-123", id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_falls_back_to_a_url_hash_without_a_guid(string? identifier)
    {
        var id = ItemIdentity.Create(identifier, new Uri("https://example.com/a"));

        Assert.StartsWith("url:", id, StringComparison.Ordinal);
        Assert.Equal(4 + 32, id.Length);
    }

    [Fact]
    public void Url_hash_is_the_same_for_equivalent_urls_and_differs_otherwise()
    {
        var a = ItemIdentity.Create(null, new Uri("https://Example.com/a/?utm_medium=rss"));
        var b = ItemIdentity.Create(null, new Uri("https://example.com/a"));
        var c = ItemIdentity.Create(null, new Uri("https://example.com/b"));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Theory]
    [InlineData("summary", "DISCORD_WEBHOOK_SUMMARY")]
    [InlineData("my_channel2", "DISCORD_WEBHOOK_MY_CHANNEL2")]
    public void WebhookVariable_derives_the_secret_name(string key, string expected) =>
        Assert.Equal(expected, ChannelKeys.WebhookVariable(key));

    [Theory]
    [InlineData("Summary")]
    [InlineData("2fast")]
    [InlineData("a-b")]
    [InlineData("")]
    public void Invalid_channel_keys_are_rejected(string key)
    {
        Assert.False(ChannelKeys.IsValid(key));
        Assert.Throws<ArgumentException>(() => ChannelKeys.WebhookVariable(key));
    }

    [Fact]
    public void Options_validation_rejects_a_seen_list_smaller_than_the_considered_window()
    {
        var options = new BotOptions { ConsiderLatest = 50, MaxSeenPerSource = 10 };

        Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Same(new BotOptions().Validate().GetType(), typeof(BotOptions));
    }
}

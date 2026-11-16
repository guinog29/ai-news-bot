using System.Net;
using System.Text.Json.Nodes;
using AiNewsBot.Core;
using AiNewsBot.Infrastructure.Discord;

namespace AiNewsBot.Infrastructure.Tests;

public class DiscordWebhookPublisherTests
{
    private const string SecretToken = "SECRET-TOKEN-123";
    private static readonly Uri OfficialWebhook = new($"https://discord.com/api/webhooks/1/{SecretToken}");
    private static readonly Uri StatusWebhook = new("https://discord.com/api/webhooks/2/other-token");
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static readonly FeedItem Item = new(
        "guid:1",
        "OpenAI",
        "official",
        "A title",
        new Uri("https://example.com/a"),
        new DateTimeOffset(2026, 11, 14, 8, 30, 0, TimeSpan.Zero));

    private static readonly Dictionary<string, Uri> Webhooks = new() { ["official"] = OfficialWebhook, ["status"] = StatusWebhook };

    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    private static HttpResponseMessage RateLimited(string retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"message\":\"rate limited\",\"retry_after\":1.5}"),
        };
        response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
    }

    private static (DiscordWebhookPublisher Publisher, List<TimeSpan> Delays) Create(HttpMessageHandler handler)
    {
        var delays = new List<TimeSpan>();
        var publisher = new DiscordWebhookPublisher(new HttpClient(handler), Webhooks, (span, _) =>
        {
            delays.Add(span);
            return Task.CompletedTask;
        });
        return (publisher, delays);
    }

    [Fact]
    public async Task Posts_an_embed_with_title_link_source_and_date_to_the_channel_webhook()
    {
        using var handler = new StubHandler(_ => NoContent());
        var (publisher, _) = Create(handler);

        await publisher.PublishAsync(Item, Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(OfficialWebhook, request.Url);
        var embed = JsonNode.Parse(request.Body)!["embeds"]![0]!;
        Assert.Equal("A title", (string?)embed["title"]);
        Assert.Equal("https://example.com/a", (string?)embed["url"]);
        Assert.Equal("OpenAI", (string?)embed["footer"]!["text"]);
        Assert.Equal("2026-11-14T08:30:00.0000000Z", (string?)embed["timestamp"]);
    }

    [Fact]
    public async Task Omits_the_timestamp_when_the_item_has_no_date_and_never_allows_mentions()
    {
        using var handler = new StubHandler(_ => NoContent());
        var (publisher, _) = Create(handler);

        await publisher.PublishAsync(Item with { PublishedAt = null }, Ct);

        var body = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.Null(body["embeds"]![0]!["timestamp"]);
        Assert.Empty(body["allowed_mentions"]!["parse"]!.AsArray());
    }

    [Fact]
    public async Task Truncates_long_titles_to_the_embed_limit()
    {
        using var handler = new StubHandler(_ => NoContent());
        var (publisher, _) = Create(handler);

        await publisher.PublishAsync(Item with { Title = new string('x', 400) }, Ct);

        var title = (string?)JsonNode.Parse(handler.Requests[0].Body)!["embeds"]![0]!["title"];
        Assert.Equal(256, title!.Length);
    }

    [Fact]
    public async Task Status_messages_go_to_the_status_webhook_as_plain_content()
    {
        using var handler = new StubHandler(_ => NoContent());
        var (publisher, _) = Create(handler);

        await publisher.PublishStatusAsync("hello", Ct);

        Assert.Equal(StatusWebhook, handler.Requests[0].Url);
        Assert.Equal("hello", (string?)JsonNode.Parse(handler.Requests[0].Body)!["content"]);
    }

    [Fact]
    public async Task Waits_for_retry_after_on_429_and_then_succeeds()
    {
        var calls = 0;
        using var handler = new StubHandler(_ => ++calls == 1 ? RateLimited("2") : NoContent());
        var (publisher, delays) = Create(handler);

        await publisher.PublishAsync(Item, Ct);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task Accepts_fractional_retry_after_values()
    {
        var calls = 0;
        using var handler = new StubHandler(_ => ++calls == 1 ? RateLimited("0.5") : NoContent());
        var (publisher, delays) = Create(handler);

        await publisher.PublishAsync(Item, Ct);

        Assert.Equal([TimeSpan.FromSeconds(0.5)], delays);
    }

    [Fact]
    public async Task Gives_up_after_three_rate_limited_attempts()
    {
        using var handler = new StubHandler(_ => RateLimited("1"));
        var (publisher, _) = Create(handler);

        await Assert.ThrowsAsync<DiscordPublishException>(() => publisher.PublishAsync(Item, Ct));

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Does_not_wait_longer_than_the_cap_for_a_rate_limit()
    {
        using var handler = new StubHandler(_ => RateLimited("600"));
        var (publisher, delays) = Create(handler);

        await Assert.ThrowsAsync<DiscordPublishException>(() => publisher.PublishAsync(Item, Ct));

        Assert.Single(handler.Requests);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Retries_server_errors_and_then_succeeds()
    {
        var calls = 0;
        using var handler = new StubHandler(_ => ++calls < 3 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : NoContent());
        var (publisher, _) = Create(handler);

        await publisher.PublishAsync(Item, Ct);

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Client_errors_fail_at_once_without_leaking_the_webhook_url()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var (publisher, _) = Create(handler);

        var error = await Assert.ThrowsAsync<DiscordPublishException>(() => publisher.PublishAsync(Item, Ct));

        Assert.Single(handler.Requests);
        Assert.Contains("404", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretToken, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Network_errors_are_retried_and_reported_without_the_webhook_url()
    {
        using var handler = new ThrowingHandler();
        var (publisher, _) = Create(handler);

        var error = await Assert.ThrowsAsync<DiscordPublishException>(() => publisher.PublishAsync(Item, Ct));

        Assert.Equal(3, handler.Calls);
        Assert.DoesNotContain(SecretToken, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paces_itself_when_the_rate_limit_bucket_is_empty()
    {
        using var handler = new StubHandler(_ =>
        {
            var response = NoContent();
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "1.25");
            return response;
        });
        var (publisher, delays) = Create(handler);

        await publisher.PublishAsync(Item, Ct);

        Assert.Equal([TimeSpan.FromSeconds(1.25)], delays);
    }

    [Fact]
    public async Task A_channel_without_a_webhook_names_the_variable_to_set()
    {
        using var handler = new StubHandler(_ => NoContent());
        var (publisher, _) = Create(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(Item with { ChannelKey = "summary" }, Ct));

        Assert.Contains("DISCORD_WEBHOOK_SUMMARY", error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Rejects_non_https_webhooks()
    {
        var insecure = new Dictionary<string, Uri> { ["official"] = new("http://discord.com/api/webhooks/1/x") };

        Assert.Throws<ArgumentException>(() => new DiscordWebhookPublisher(new HttpClient(), insecure));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new HttpRequestException($"failure talking to {request.RequestUri}");
        }
    }
}

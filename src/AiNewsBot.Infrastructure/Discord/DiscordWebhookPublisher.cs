using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AiNewsBot.Core;

namespace AiNewsBot.Infrastructure.Discord;

/// <summary>Discord rejected the post or could not be reached. The message never contains the webhook URL (D9).</summary>
public sealed class DiscordPublishException : Exception
{
    /// <summary>Creates the exception.</summary>
    public DiscordPublishException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public DiscordPublishException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public DiscordPublishException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Posts to Discord webhooks, one per channel key (D16, D13, D18).</summary>
public sealed class DiscordWebhookPublisher : INewsPublisher
{
    private const int MaxAttempts = 3;
    private const int EmbedColor = 0x5865F2;
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly IReadOnlyDictionary<string, Uri> _webhooks;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Creates the publisher.</summary>
    /// <param name="http">Client to send requests with.</param>
    /// <param name="webhooks">Webhook URL per channel key. All must be https.</param>
    /// <param name="delay">Waits between retries; replaced in tests.</param>
    public DiscordWebhookPublisher(
        HttpClient http,
        IReadOnlyDictionary<string, Uri> webhooks,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(webhooks);
        foreach (var (key, url) in webhooks)
        {
            if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException($"The webhook for channel '{key}' must be an absolute https URL.", nameof(webhooks));
            }
        }

        _http = http;
        _webhooks = webhooks;
        _delay = delay ?? ((span, ct) => Task.Delay(span, ct));
    }

    /// <inheritdoc />
    public Task PublishAsync(FeedItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var embed = new JsonObject
        {
            ["title"] = Truncate(item.Title, 256),
            ["url"] = item.Url.AbsoluteUri,
            ["color"] = EmbedColor,
            ["footer"] = new JsonObject { ["text"] = Truncate(item.SourceName, 2048) },
        };
        if (item.PublishedAt is { } published)
        {
            embed["timestamp"] = published.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        }

        var payload = new JsonObject
        {
            ["embeds"] = new JsonArray(embed),
            ["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray() },
        };
        return SendAsync(item.ChannelKey, payload, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishStatusAsync(string message, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["content"] = Truncate(message, 2000),
            ["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray() },
        };
        return SendAsync(ChannelKeys.Status, payload, cancellationToken);
    }

    private async Task SendAsync(string channelKey, JsonObject payload, CancellationToken ct)
    {
        if (!_webhooks.TryGetValue(channelKey, out var webhook))
        {
            throw new InvalidOperationException(
                $"No webhook configured for channel '{channelKey}' (expected variable {ChannelKeys.WebhookVariable(channelKey)}).");
        }

        for (var attempt = 1; ; attempt++)
        {
            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsync(webhook, content, ct);
            }
            catch (HttpRequestException ex)
            {
                // The inner exception is not exposed on purpose: keep the URL out of every message and log.
                if (attempt == MaxAttempts)
                {
                    throw new DiscordPublishException($"Could not reach Discord ({ex.HttpRequestError}).");
                }

                await _delay(TimeSpan.FromSeconds(2 * attempt), ct);
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    await PaceAsync(response, ct);
                    return;
                }

                var status = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var wait = await RetryAfterAsync(response, ct);
                    if (attempt == MaxAttempts || wait > MaxWait)
                    {
                        throw new DiscordPublishException($"Discord rate limit not cleared (retry after {wait.TotalSeconds:0.#}s).");
                    }

                    await _delay(wait, ct);
                }
                else if (status >= 500 && attempt < MaxAttempts)
                {
                    await _delay(TimeSpan.FromSeconds(2 * attempt), ct);
                }
                else
                {
                    throw new DiscordPublishException($"Discord returned HTTP {status}.");
                }
            }
        }
    }

    /// <summary>Waits when the bucket is exhausted so the next post does not hit a 429 (D13).</summary>
    private async Task PaceAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (Header(response, "X-RateLimit-Remaining") == "0"
            && TryParseSeconds(Header(response, "X-RateLimit-Reset-After"), out var reset)
            && reset <= MaxWait)
        {
            await _delay(reset, ct);
        }
    }

    private static async Task<TimeSpan> RetryAfterAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (TryParseSeconds(Header(response, "Retry-After"), out var fromHeader))
        {
            return fromHeader;
        }

        try
        {
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            if (body?["retry_after"]?.GetValue<double>() is { } seconds)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            // Fall through to the default below.
        }

        return TimeSpan.FromSeconds(1);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.NonValidated.TryGetValues(name, out var values) ? values.ToString() : null;

    private static bool TryParseSeconds(string? text, out TimeSpan span)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
        {
            span = TimeSpan.FromSeconds(seconds);
            return true;
        }

        span = default;
        return false;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}

using AiNewsBot.Core;

namespace AiNewsBot.Infrastructure.Discord;

/// <summary>Resolves webhook URLs from environment variables named <c>DISCORD_WEBHOOK_&lt;KEY&gt;</c> (D9, D18).</summary>
public static class DiscordWebhookConfig
{
    /// <summary>
    /// Reads the webhook of every required channel key plus the optional status channel.
    /// Errors name the variables only, never their values.
    /// </summary>
    /// <param name="requiredKeys">Channel keys used by the configured sources.</param>
    /// <param name="getVariable">Reads an environment variable.</param>
    /// <exception cref="InvalidOperationException">A required variable is missing, or any value is not a valid https URL.</exception>
    public static IReadOnlyDictionary<string, Uri> Resolve(IEnumerable<string> requiredKeys, Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(requiredKeys);
        ArgumentNullException.ThrowIfNull(getVariable);

        var required = requiredKeys.ToHashSet(StringComparer.Ordinal);
        var webhooks = new Dictionary<string, Uri>(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var key in required.Append(ChannelKeys.Status).Distinct(StringComparer.Ordinal))
        {
            var variable = ChannelKeys.WebhookVariable(key);
            var value = getVariable(variable);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (required.Contains(key))
                {
                    problems.Add($"{variable} is not set");
                }

                continue;
            }

            if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
            {
                webhooks[key] = url;
            }
            else
            {
                problems.Add($"{variable} is not a valid https URL");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Webhook configuration problem: " + string.Join("; ", problems) + ".");
        }

        return webhooks;
    }
}

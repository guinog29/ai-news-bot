using System.Text.RegularExpressions;

namespace AiNewsBot.Core;

/// <summary>Channel key convention (D18): config holds a key, the secret name is derived from it.</summary>
public static partial class ChannelKeys
{
    /// <summary>Key of the channel that receives failure alerts.</summary>
    public const string Status = "status";

    /// <summary>Whether <paramref name="key"/> is a valid lowercase channel key.</summary>
    public static bool IsValid(string? key) => key is not null && KeyPattern().IsMatch(key);

    /// <summary>Name of the environment variable / secret holding the webhook URL for <paramref name="key"/>.</summary>
    public static string WebhookVariable(string key)
    {
        if (!IsValid(key))
        {
            throw new ArgumentException($"Invalid channel key '{key}'. Use lowercase letters, digits and underscores.", nameof(key));
        }

        return "DISCORD_WEBHOOK_" + key.ToUpperInvariant();
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex KeyPattern();
}

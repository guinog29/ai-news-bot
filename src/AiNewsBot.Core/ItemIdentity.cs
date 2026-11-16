using System.Security.Cryptography;
using System.Text;

namespace AiNewsBot.Core;

/// <summary>Builds stable item identifiers (D6): the GUID when present, otherwise a hash of the normalized URL.</summary>
public static class ItemIdentity
{
    /// <summary>Identifier for an item, preferring <paramref name="guidValue"/> over <paramref name="url"/>.</summary>
    public static string Create(string? guidValue, Uri url) =>
        string.IsNullOrWhiteSpace(guidValue) ? "url:" + HashUrl(url) : "guid:" + guidValue.Trim();

    private static string HashUrl(Uri url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(UrlNormalizer.Normalize(url)));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}

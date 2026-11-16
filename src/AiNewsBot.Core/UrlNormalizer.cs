using System.Text;

namespace AiNewsBot.Core;

/// <summary>Reduces URLs that point to the same page to one string (used for D6 hashing).</summary>
public static class UrlNormalizer
{
    private static readonly string[] TrackingParameters = ["fbclid", "gclid", "mc_cid", "mc_eid"];

    /// <summary>Lowercases scheme and host, drops fragment, default port, trailing slash and tracking parameters, and sorts the query.</summary>
    public static string Normalize(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri)
        {
            throw new ArgumentException("URL must be absolute.", nameof(url));
        }

        var builder = new StringBuilder();
        builder.Append(url.Scheme).Append("://").Append(url.Host);
        if (!url.IsDefaultPort)
        {
            builder.Append(':').Append(url.Port);
        }

        var path = url.AbsolutePath;
        builder.Append(path.Length > 1 ? path.TrimEnd('/') : path);

        var query = url.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !IsTracking(p))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (query.Count > 0)
        {
            builder.Append('?').AppendJoin('&', query);
        }

        return builder.ToString();
    }

    private static bool IsTracking(string parameter)
    {
        var name = parameter.Split('=', 2)[0];
        return name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
            || TrackingParameters.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}

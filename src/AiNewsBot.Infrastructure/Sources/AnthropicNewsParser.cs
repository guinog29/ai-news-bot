using System.Globalization;
using System.Text.RegularExpressions;
using AiNewsBot.Core;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace AiNewsBot.Infrastructure.Sources;

/// <summary>
/// Extracts articles from anthropic.com/news (D12). The page uses generated CSS class names, so this relies on
/// structure instead: a link to <c>/news/{slug}</c> that contains a heading is an article card, and the
/// <c>&lt;time&gt;</c> inside the enclosing <c>article</c>/<c>li</c> is its date. Category links have no heading.
/// </summary>
public static partial class AnthropicNewsParser
{
    private static readonly string[] DateFormats = ["MMM d, yyyy", "MMMM d, yyyy"];

    /// <summary>Parses the page. Returns an empty list when nothing matches; the job treats that as a failure.</summary>
    public static IReadOnlyList<FeedItem> Parse(string html, FeedSourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(source);

        var document = new HtmlParser().ParseDocument(html);
        var found = new Dictionary<string, (string Title, DateTimeOffset? Date)>(StringComparer.Ordinal);

        foreach (var anchor in document.QuerySelectorAll("a[href^='/news/']"))
        {
            var href = anchor.GetAttribute("href") ?? string.Empty;
            var title = anchor.QuerySelector("h3")?.TextContent;
            if (!ArticlePath().IsMatch(href) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var date = ReadDate(anchor);
            found[href] = found.TryGetValue(href, out var existing)
                ? (existing.Title, existing.Date ?? date)
                : (Collapse(title), date);
        }

        return found
            .Select(pair =>
            {
                var url = new Uri(source.Url, pair.Key);
                return new FeedItem(ItemIdentity.Create(null, url), source.Name, source.Channel, pair.Value.Title, url, pair.Value.Date);
            })
            .ToList();
    }

    private static DateTimeOffset? ReadDate(IElement anchor)
    {
        var text = anchor.Closest("article, li")?.QuerySelector("time")?.TextContent.Trim();
        return DateTimeOffset.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
    }

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex("^/news/[^/?#]+$")]
    private static partial Regex ArticlePath();
}

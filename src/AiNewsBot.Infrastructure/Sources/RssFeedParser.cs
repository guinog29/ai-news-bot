using System.ServiceModel.Syndication;
using System.Xml;
using AiNewsBot.Core;

namespace AiNewsBot.Infrastructure.Sources;

/// <summary>Turns an RSS or Atom document into <see cref="FeedItem"/>s. Pure: no network.</summary>
public static class RssFeedParser
{
    /// <summary>Parses the feed. Entries without a title or a usable http(s) link are ignored.</summary>
    /// <exception cref="XmlException">The document is not a valid feed.</exception>
    public static IReadOnlyList<FeedItem> Parse(Stream feed, FeedSourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(source);

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(feed, settings);
        var syndication = SyndicationFeed.Load(reader);

        var items = new List<FeedItem>();
        foreach (var entry in syndication.Items)
        {
            var url = ResolveLink(entry, source.Url);
            var title = Clean(entry.Title?.Text);
            if (url is null || title.Length == 0)
            {
                continue;
            }

            items.Add(new FeedItem(
                ItemIdentity.Create(entry.Id, url),
                source.Name,
                source.Channel,
                title,
                url,
                PublishedAt(entry)));
        }

        return items;
    }

    private static Uri? ResolveLink(SyndicationItem entry, Uri feedUrl)
    {
        var link = entry.Links.FirstOrDefault(l => l.RelationshipType is null or "alternate")
            ?? entry.Links.FirstOrDefault();
        var uri = link?.Uri;
        if (uri is null)
        {
            return null;
        }

        if (!uri.IsAbsoluteUri)
        {
            uri = new Uri(feedUrl, uri);
        }

        return uri.Scheme is "http" or "https" ? uri : null;
    }

    private static DateTimeOffset? PublishedAt(SyndicationItem entry)
    {
        if (entry.PublishDate != default)
        {
            return entry.PublishDate;
        }

        return entry.LastUpdatedTime != default ? entry.LastUpdatedTime : null;
    }

    private static string Clean(string? text) =>
        text is null ? string.Empty : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

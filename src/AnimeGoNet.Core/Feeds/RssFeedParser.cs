using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace AnimeGoNet.Core.Feeds;

public static class RssFeedParser
{
    public const int MaximumBytes = 5 * 1024 * 1024;

    public static RssFeedDocument Parse(ReadOnlyMemory<byte> raw, string? sourceUrl = null)
    {
        if (raw.IsEmpty)
        {
            throw new RssFeedException("rss_empty", "RSS content is empty.");
        }

        if (raw.Length > MaximumBytes)
        {
            throw new RssFeedException("rss_too_large", "RSS content exceeds the size limit.");
        }

        XDocument document;
        try
        {
            using var stream = new MemoryStream(raw.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            throw new RssFeedException("rss_parse_failed", "RSS XML could not be parsed.", exception);
        }

        var channel = document.Root?.Name.LocalName == "rss"
            ? document.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "channel")
            : null;
        if (channel is null)
        {
            throw new RssFeedException("rss_parse_failed", "RSS channel is missing.");
        }

        var items = new List<RssFeedItem>();
        foreach (var item in channel.Elements().Where(element => element.Name.LocalName == "item"))
        {
            var enclosure = item.Elements().FirstOrDefault(element => element.Name.LocalName == "enclosure");
            var torrentUrl = enclosure?.Attribute("url")?.Value?.Trim();
            if (enclosure is null || string.IsNullOrWhiteSpace(torrentUrl))
            {
                continue;
            }

            var lengthValue = enclosure.Attribute("length")?.Value;
            var length = long.TryParse(lengthValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedLength)
                && parsedLength >= 0
                ? parsedLength
                : 0;
            var title = ChildValue(item, "title") ?? string.Empty;
            var mikanUrl = item.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "link" && element.Name.NamespaceName.Length == 0)
                ?.Value.Trim() ?? string.Empty;
            var published = item.Descendants().FirstOrDefault(element => element.Name.LocalName == "pubDate")
                ?.Value.Trim();

            items.Add(new RssFeedItem(
                title,
                mikanUrl,
                torrentUrl,
                enclosure.Attribute("type")?.Value?.Trim() ?? string.Empty,
                length,
                string.IsNullOrWhiteSpace(published) ? null : published));
        }

        var mikanId = MikanIdentityParser.TryParseMikanId(sourceUrl)
            ?? MikanIdentityParser.TryParseMikanId(ChildValue(channel, "link"));
        return new RssFeedDocument(items, mikanId);
    }

    private static string? ChildValue(XElement parent, string localName)
    {
        var value = parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

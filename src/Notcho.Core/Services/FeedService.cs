using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shiny;

namespace Notcho.Services;

/// <summary>
/// Fetches and reads RSS 2.0, RSS 1.0 and Atom feeds, for the News and RSS tabs. Each feed is cached for
/// ten minutes (a minute after a failure), so the tabs can ask as often as they like.
/// </summary>
/// <remarks>
/// Elements are matched by local name, whatever namespace a feed puts them in, which covers the three
/// formats and the odd feed that mixes them. Text is decoded and stripped of markup: the tabs show
/// plain lines, never the feed's HTML.
/// </remarks>
[Singleton]
public sealed partial class FeedService
{
    static readonly TimeSpan Stale = TimeSpan.FromMinutes(10);
    static readonly TimeSpan FailedRetry = TimeSpan.FromMinutes(1);
    const int MaxSummary = 220;

    readonly TimeProvider time;
    readonly HttpClient http;
    readonly Dictionary<string, Feed> feeds = new();
    readonly Dictionary<string, Task<bool>> loads = new();

    public FeedService(TimeProvider time)
    {
        this.time = time;
        this.http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(15) };
        // Some publishers turn away requests that don't look like a browser, and CBC drops the
        // "compatible; …; +url" form crawlers use, so it reads as a Mac browser that names Notcho.
        this.http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Notcho/1.0");
    }

    DateTime UtcNow => this.time.GetUtcNow().UtcDateTime;

    /// <summary>The feed as last fetched; null before its first answer.</summary>
    public Feed? Get(string url) => this.feeds.GetValueOrDefault(url);

    /// <summary>
    /// Fetches the feed if it has gone stale. True when what it holds changed, which is what the tabs
    /// rebuild on. A fetch already under way is shared, not repeated.
    /// </summary>
    public Task<bool> RefreshAsync(string url)
    {
        var old = this.feeds.GetValueOrDefault(url);
        if (old != null && this.UtcNow - old.FetchedUtc < (old.Failed ? FailedRetry : Stale))
            return Task.FromResult(false);
        if (this.loads.TryGetValue(url, out var loading))
            return loading;

        var load = this.LoadAsync(url, old);
        this.loads[url] = load;
        return load;
    }

    async Task<bool> LoadAsync(string url, Feed? old)
    {
        try
        {
            var fresh = await this.FetchAsync(url).ConfigureAwait(true);
            if (fresh == null)
            {
                // Keep what was there to show; only mark it to be tried again soon.
                this.feeds[url] = (old ?? new Feed(url, "", [], this.UtcNow)) with { FetchedUtc = this.UtcNow, Failed = true };
                return old == null;
            }
            this.feeds[url] = fresh;
            return old == null || old.Failed || !old.Items.Select(i => i.Link).SequenceEqual(fresh.Items.Select(i => i.Link));
        }
        finally
        {
            this.loads.Remove(url);
        }
    }

    async Task<Feed?> FetchAsync(string url)
    {
        try
        {
            var text = await this.http.GetStringAsync(url).ConfigureAwait(true);
            return this.Parse(url, text);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the feed for what someone typed: a feed's own address, or a site's page that names its feed
    /// (the &lt;link rel="alternate"&gt; every blog and news site carries). Null when there's no feed there.
    /// </summary>
    public async Task<Feed?> ProbeAsync(string typed)
    {
        var address = typed.Trim();
        if (address.Length == 0)
            return null;
        if (!address.Contains("://", StringComparison.Ordinal))
            address = "https://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;

        string text;
        try
        {
            text = await this.http.GetStringAsync(uri).ConfigureAwait(true);
        }
        catch
        {
            return null;
        }

        if (this.Parse(uri.ToString(), text) is { } feed)
        {
            this.feeds[feed.Url] = feed;
            return feed;
        }

        // A web page: follow the first feed it advertises.
        var link = AlternateLink().Matches(text)
            .Select(m => m.Value)
            .Where(tag => FeedType().IsMatch(tag))
            .Select(tag => Href().Match(tag))
            .Where(m => m.Success)
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .FirstOrDefault();
        if (link == null || !Uri.TryCreate(uri, link, out var feedUri))
            return null;

        var found = await this.FetchAsync(feedUri.ToString()).ConfigureAwait(true);
        if (found != null)
            this.feeds[found.Url] = found;
        return found;
    }

    // ---------------------------------------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------------------------------------

    /// <summary>Reads a feed, or null when the text isn't one.</summary>
    Feed? Parse(string url, string text)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(text);
        }
        catch
        {
            return null;
        }

        var root = doc.Root;
        if (root == null || root.Name.LocalName is not ("rss" or "feed" or "RDF"))
            return null;

        var channel = root.Name.LocalName == "rss" ? Child(root, "channel") ?? root : root;
        var title = Clean(Value(Child(channel, "title"))) ?? new Uri(url).Host;
        var items = root.Descendants()
            .Where(e => e.Name.LocalName is "item" or "entry")
            .Select(e => ParseItem(e, title, url))
            .OfType<FeedItem>()
            .ToList();
        return new Feed(url, title, items, this.UtcNow);
    }

    static FeedItem? ParseItem(XElement e, string source, string feedUrl)
    {
        var title = Clean(Value(Child(e, "title")));
        var link = ItemLink(e);
        if (title == null || link == null || !Uri.TryCreate(new Uri(feedUrl), link, out var absolute))
            return null;

        var published = ParseDate(Value(Child(e, "pubDate") ?? Child(e, "published") ?? Child(e, "updated") ?? Child(e, "date")));
        var summary = Clean(Value(Child(e, "description") ?? Child(e, "summary") ?? Child(e, "content")));
        // A summary that only repeats the headline says nothing.
        if (summary != null && summary.Equals(title, StringComparison.OrdinalIgnoreCase))
            summary = null;
        if (summary is { Length: > MaxSummary })
            summary = summary[..MaxSummary].TrimEnd() + "…";
        return new FeedItem(title, absolute.ToString(), published, source, summary);
    }

    /// <summary>RSS puts the link in the element's text; Atom in an href, preferring rel="alternate".</summary>
    static string? ItemLink(XElement e)
    {
        var links = e.Elements().Where(x => x.Name.LocalName == "link").ToList();
        var atom = links.FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate" && l.Attribute("href") != null);
        var href = (string?)atom?.Attribute("href");
        if (href != null)
            return href.Trim();
        var text = links.Select(l => l.Value.Trim()).FirstOrDefault(v => v.Length > 0);
        if (text != null)
            return text;
        // Some RSS feeds leave the link out and only give the guid, as a permalink.
        var guid = Value(Child(e, "guid"))?.Trim();
        return guid != null && guid.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? guid : null;
    }

    static XElement? Child(XElement e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name);

    static string? Value(XElement? e) => e?.Value is { Length: > 0 } v ? v : null;

    /// <summary>Plain text: tags stripped, entities decoded, runs of whitespace made one space.</summary>
    static string? Clean(string? html)
    {
        if (html == null)
            return null;
        var text = WebUtility.HtmlDecode(Tags().Replace(html, " "));
        text = Spaces().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// RFC 822 dates as RSS writes them ("Thu, 27 Nov 2026 14:05:00 EST", with the zones .NET doesn't
    /// read swapped for offsets), and ISO 8601 as Atom does.
    /// </summary>
    static DateTimeOffset? ParseDate(string? text)
    {
        if (text == null)
            return null;
        var value = text.Trim();
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed;

        // Drop the day name, then turn a zone name into an offset.
        var comma = value.IndexOf(',');
        if (comma >= 0)
            value = value[(comma + 1)..].Trim();
        var space = value.LastIndexOf(' ');
        if (space > 0 && Zones.TryGetValue(value[(space + 1)..], out var offset))
            value = value[..space] + " " + offset;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed) ? parsed : null;
    }

    static readonly Dictionary<string, string> Zones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMT"] = "+00:00", ["UT"] = "+00:00", ["UTC"] = "+00:00", ["Z"] = "+00:00",
        ["EST"] = "-05:00", ["EDT"] = "-04:00", ["CST"] = "-06:00", ["CDT"] = "-05:00",
        ["MST"] = "-07:00", ["MDT"] = "-06:00", ["PST"] = "-08:00", ["PDT"] = "-07:00",
        ["AST"] = "-04:00", ["ADT"] = "-03:00", ["NST"] = "-03:30", ["NDT"] = "-02:30",
        ["BST"] = "+01:00", ["CET"] = "+01:00", ["CEST"] = "+02:00"
    };

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("<link[^>]+rel=[\"']?alternate[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex AlternateLink();

    [GeneratedRegex("application/(rss|atom)\\+xml", RegexOptions.IgnoreCase)]
    private static partial Regex FeedType();

    [GeneratedRegex("href=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex Href();
}

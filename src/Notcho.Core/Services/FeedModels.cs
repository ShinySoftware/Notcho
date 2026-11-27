namespace Notcho.Services;

/// <summary>A story from an RSS or Atom feed, with what the list shows and where a click goes.</summary>
/// <param name="Source">Who it's from: the network's name, or the feed's own title.</param>
/// <param name="Summary">The feed's description, as plain text; null when it has none.</param>
public sealed record FeedItem(string Title, string Link, DateTimeOffset? Published, string Source, string? Summary);

/// <summary>A feed as last fetched.</summary>
/// <param name="Title">The feed's own name for itself.</param>
/// <param name="Failed">The last fetch failed; <see cref="Items"/> are from an earlier one, if any.</param>
public sealed record Feed(string Url, string Title, IReadOnlyList<FeedItem> Items, DateTime FetchedUtc, bool Failed = false);

/// <summary>A feed the user added to the RSS tab, under the title it gave when it was added.</summary>
public sealed record RssFeed(string Url, string Title);

/// <summary>A kind of story the News tab can show, across every network that has it.</summary>
public sealed record NewsCategory(string Key, string Name)
{
    /// <summary>Every category, in the order the tab's chips and Settings list them.</summary>
    public static readonly IReadOnlyList<NewsCategory> All =
    [
        new("top", "Top Stories"),
        new("world", "World"),
        new("canada", "Canada"),
        new("us", "U.S."),
        new("uk", "U.K."),
        new("politics", "Politics"),
        new("business", "Business"),
        new("tech", "Technology"),
        new("science", "Science"),
        new("health", "Health"),
        new("sports", "Sports"),
        new("arts", "Arts & Entertainment"),
        new("opinion", "Opinion")
    ];

    /// <summary>Shown until changed in Settings.</summary>
    public static readonly IReadOnlyList<string> Defaults = ["top", "world"];

    public static NewsCategory? Find(string key) => All.FirstOrDefault(c => c.Key == key);
}

/// <summary>A news network and the feed it publishes for each category it covers.</summary>
/// <param name="Note">Said under its switch in Settings, when there's something to know.</param>
/// <param name="TitleSuffix">Trimmed off the end of each headline (" - CNN" on Google News' copies).</param>
public sealed record NewsNetwork(string Key, string Name, string Color, IReadOnlyDictionary<string, string> Feeds, string? Note = null, string? TitleSuffix = null)
{
    public static NewsNetwork? Find(string key) => All.FirstOrDefault(n => n.Key == key);

    /// <summary>Shown until changed in Settings.</summary>
    public static readonly IReadOnlyList<string> Defaults = ["cbc", "bbc"];

    const string Ctv = "https://www.ctvnews.ca/arc/outboundfeeds/rss/";
    const string Global = "https://globalnews.ca/";
    const string Fox = "https://moxie.foxnews.com/google-publisher/";
    const string Nbc = "https://feeds.nbcnews.com/nbcnews/public/";
    const string Cbs = "https://www.cbsnews.com/latest/rss/";
    const string Npr = "https://feeds.npr.org/";
    const string Bbc = "https://feeds.bbci.co.uk/news/";
    const string Guardian = "https://www.theguardian.com/";
    // CNN stopped updating its own RSS feeds in 2023; Google News' search of its site is current. A week
    // back, because the search isn't in date order and would otherwise mix in stories months old.
    const string Cnn = "https://news.google.com/rss/search?hl=en-US&gl=US&ceid=US:en&q=when:7d+site:cnn.com";

    /// <summary>
    /// Every network on offer, each with the categories its feeds cover. All checked to answer with
    /// current stories; a category a network doesn't publish is simply missing from its list.
    /// </summary>
    public static readonly IReadOnlyList<NewsNetwork> All =
    [
        new("cbc", "CBC News", "#E60505", new Dictionary<string, string>
        {
            ["top"] = "https://www.cbc.ca/webfeed/rss/rss-topstories",
            ["world"] = "https://www.cbc.ca/webfeed/rss/rss-world",
            ["canada"] = "https://www.cbc.ca/webfeed/rss/rss-canada",
            ["politics"] = "https://www.cbc.ca/webfeed/rss/rss-politics",
            ["business"] = "https://www.cbc.ca/webfeed/rss/rss-business",
            ["tech"] = "https://www.cbc.ca/webfeed/rss/rss-technology",
            ["health"] = "https://www.cbc.ca/webfeed/rss/rss-health",
            ["sports"] = "https://www.cbc.ca/webfeed/rss/rss-sports",
            ["arts"] = "https://www.cbc.ca/webfeed/rss/rss-arts"
        }),
        new("ctv", "CTV News", "#0067B9", new Dictionary<string, string>
        {
            ["top"] = Ctv + "?outputType=xml",
            ["world"] = Ctv + "category/world/?outputType=xml",
            ["canada"] = Ctv + "category/canada/?outputType=xml",
            ["politics"] = Ctv + "category/politics/?outputType=xml",
            ["business"] = Ctv + "category/business/?outputType=xml",
            ["tech"] = Ctv + "category/sci-tech/?outputType=xml",
            ["health"] = Ctv + "category/health/?outputType=xml",
            ["sports"] = Ctv + "category/sports/?outputType=xml",
            ["arts"] = Ctv + "category/entertainment/?outputType=xml"
        }),
        new("global", "Global News", "#00A3E0", new Dictionary<string, string>
        {
            ["top"] = Global + "feed/",
            ["world"] = Global + "world/feed/",
            ["canada"] = Global + "canada/feed/",
            ["politics"] = Global + "politics/feed/",
            ["business"] = Global + "money/feed/",
            ["tech"] = Global + "tech/feed/",
            ["health"] = Global + "health/feed/",
            ["sports"] = Global + "sports/feed/",
            ["arts"] = Global + "entertainment/feed/"
        }),
        new("cnn", "CNN", "#CC0000", new Dictionary<string, string>
        {
            ["top"] = Cnn,
            ["world"] = Cnn + "/world",
            ["politics"] = Cnn + "/politics",
            ["sports"] = Cnn + "/sport"
        }, Note: "Through Google News: CNN no longer updates its own feeds.", TitleSuffix: " - CNN"),
        new("fox", "Fox News", "#003366", new Dictionary<string, string>
        {
            ["top"] = Fox + "latest.xml",
            ["world"] = Fox + "world.xml",
            ["us"] = Fox + "us.xml",
            ["politics"] = Fox + "politics.xml",
            ["business"] = "https://moxie.foxbusiness.com/google-publisher/latest.xml",
            ["tech"] = Fox + "tech.xml",
            ["health"] = Fox + "health.xml",
            ["sports"] = Fox + "sports.xml",
            ["opinion"] = Fox + "opinion.xml"
        }),
        new("nbc", "NBC News", "#6460AA", new Dictionary<string, string>
        {
            ["top"] = Nbc + "news",
            ["world"] = Nbc + "world",
            ["politics"] = Nbc + "politics",
            ["business"] = Nbc + "business",
            ["tech"] = Nbc + "tech",
            ["health"] = Nbc + "health"
        }),
        new("cbs", "CBS News", "#2A6EBB", new Dictionary<string, string>
        {
            ["top"] = Cbs + "main",
            ["world"] = Cbs + "world",
            ["us"] = Cbs + "us",
            ["politics"] = Cbs + "politics",
            ["business"] = Cbs + "moneywatch",
            ["tech"] = Cbs + "technology",
            ["science"] = Cbs + "science",
            ["health"] = Cbs + "health",
            ["arts"] = Cbs + "entertainment"
        }),
        new("npr", "NPR", "#D62021", new Dictionary<string, string>
        {
            ["top"] = Npr + "1001/rss.xml",
            ["world"] = Npr + "1004/rss.xml",
            ["us"] = Npr + "1003/rss.xml",
            ["politics"] = Npr + "1014/rss.xml",
            ["business"] = Npr + "1006/rss.xml",
            ["tech"] = Npr + "1019/rss.xml",
            ["science"] = Npr + "1007/rss.xml",
            ["health"] = Npr + "1128/rss.xml",
            ["arts"] = Npr + "1008/rss.xml"
        }),
        new("bbc", "BBC News", "#BB1919", new Dictionary<string, string>
        {
            ["top"] = Bbc + "rss.xml",
            ["world"] = Bbc + "world/rss.xml",
            ["uk"] = Bbc + "uk/rss.xml",
            ["politics"] = Bbc + "politics/rss.xml",
            ["business"] = Bbc + "business/rss.xml",
            ["tech"] = Bbc + "technology/rss.xml",
            ["science"] = Bbc + "science_and_environment/rss.xml",
            ["health"] = Bbc + "health/rss.xml",
            ["sports"] = "https://feeds.bbci.co.uk/sport/rss.xml",
            ["arts"] = Bbc + "entertainment_and_arts/rss.xml"
        }),
        new("guardian", "The Guardian", "#052962", new Dictionary<string, string>
        {
            ["top"] = Guardian + "international/rss",
            ["world"] = Guardian + "world/rss",
            ["us"] = Guardian + "us-news/rss",
            ["uk"] = Guardian + "uk-news/rss",
            ["politics"] = Guardian + "politics/rss",
            ["business"] = Guardian + "business/rss",
            ["tech"] = Guardian + "technology/rss",
            ["science"] = Guardian + "science/rss",
            ["sports"] = Guardian + "sport/rss",
            ["arts"] = Guardian + "culture/rss"
        }),
        new("aljazeera", "Al Jazeera", "#FA9000", new Dictionary<string, string>
        {
            ["top"] = "https://www.aljazeera.com/xml/rss/all.xml"
        }, Note: "It publishes one feed, of everything.")
    ];
}

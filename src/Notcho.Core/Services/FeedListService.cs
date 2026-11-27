using Shiny;

namespace Notcho.Services;

/// <summary>A chip across the top of the News or RSS tab.</summary>
public sealed record FeedChip(string Key, string Name);

/// <summary>A feed behind a chip, and the name its stories are shown under.</summary>
/// <param name="Color">A hex colour for that name; null for the default.</param>
/// <param name="TitleSuffix">Trimmed off the end of each headline.</param>
public sealed record FeedSource(string Url, string Name, string? Color = null, string? TitleSuffix = null);

/// <summary>
/// What the News and RSS tabs share: chips across the top, the feeds behind the chosen one, and their
/// stories merged newest first. Only the chosen chip's feeds are fetched, each when it goes stale.
/// </summary>
public abstract class FeedListService
{
    /// <summary>Stories shown at most: a notch is for a glance, not an afternoon.</summary>
    const int MaxItems = 60;

    protected readonly AppSettings Settings;
    readonly FeedService feeds;
    bool refreshing;
    bool refreshQueued;

    protected FeedListService(AppSettings settings, FeedService feeds)
    {
        this.Settings = settings;
        this.feeds = feeds;
    }

    /// <summary>New stories arrived, or the chips or the chosen one changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The chips, in order. Empty when nothing is set up in Settings yet.</summary>
    public abstract IReadOnlyList<FeedChip> Chips { get; }

    /// <summary>The feeds behind a chip.</summary>
    protected abstract IReadOnlyList<FeedSource> SourcesFor(string chip);

    protected abstract string? StoredFilter { get; set; }

    /// <summary>The chosen chip: the one last picked while it's still there, else the first.</summary>
    public string? Filter
    {
        get
        {
            var chips = this.Chips;
            var chosen = this.StoredFilter;
            return chips.Any(c => c.Key == chosen) ? chosen : chips.FirstOrDefault()?.Key;
        }
    }

    public void SetFilter(string key)
    {
        this.StoredFilter = key;
        this.Raise();
        _ = this.RefreshAsync();
    }

    /// <summary>The chosen chip's stories, newest first, each link once.</summary>
    /// <param name="loading">Some feed hasn't answered yet.</param>
    /// <param name="failed">Some feed couldn't be read.</param>
    public IReadOnlyList<(FeedItem Item, FeedSource Source)> Items(out bool loading, out bool failed)
    {
        loading = false;
        failed = false;
        var all = new List<(FeedItem, FeedSource)>();
        if (this.Filter is not { } chip)
            return all;

        foreach (var source in this.SourcesFor(chip))
        {
            var feed = this.feeds.Get(source.Url);
            loading |= feed == null;
            failed |= feed is { Failed: true, Items.Count: 0 };
            foreach (var item in feed?.Items ?? [])
                all.Add((Trim(item, source), source));
        }

        return all
            .DistinctBy(x => x.Item1.Link)
            .OrderByDescending(x => x.Item1.Published ?? DateTimeOffset.MinValue)
            .Take(MaxItems)
            .ToList();
    }

    static FeedItem Trim(FeedItem item, FeedSource source)
        => source.TitleSuffix != null && item.Title.EndsWith(source.TitleSuffix, StringComparison.Ordinal)
            ? item with { Title = item.Title[..^source.TitleSuffix.Length] }
            : item;

    /// <summary>
    /// Fetches the chosen chip's stale feeds, all at once. A feed answering for the first time shows
    /// straight away, rather than waiting on the slowest; later changes raise <see cref="Changed"/> once
    /// at the end, so a list being read isn't rebuilt under the reader feed by feed. Asked while one
    /// runs, it runs again after.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (this.refreshing)
        {
            this.refreshQueued = true;
            return;
        }

        this.refreshing = true;
        var changed = false;
        try
        {
            do
            {
                this.refreshQueued = false;
                var sources = this.Filter is { } chip ? this.SourcesFor(chip) : [];
                var results = await Task.WhenAll(sources.Select(this.RefreshAsync)).ConfigureAwait(true);
                changed |= results.Any(r => r);
            }
            while (this.refreshQueued);
        }
        finally
        {
            this.refreshing = false;
        }

        if (changed)
            this.Raise();
    }

    /// <summary>One feed; true when it changed and is still to be shown.</summary>
    async Task<bool> RefreshAsync(FeedSource source)
    {
        var first = this.feeds.Get(source.Url) == null;
        var changed = await this.feeds.RefreshAsync(source.Url).ConfigureAwait(true);
        if (!changed || !first)
            return changed;
        this.Raise();
        return false;
    }

    protected void Raise() => this.Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Headlines from the news networks you pick, a chip per category you pick: each category's stories from
/// every chosen network that covers it, newest first.
/// </summary>
[Singleton]
public sealed class NewsService : FeedListService
{
    public NewsService(AppSettings settings, FeedService feeds) : base(settings, feeds)
    {
        settings.Changed += (_, key) =>
        {
            if (key is not (nameof(AppSettings.NewsNetworks) or nameof(AppSettings.NewsCategories)))
                return;
            this.Raise();
            _ = this.RefreshAsync();
        };
    }

    public IReadOnlyList<NewsNetwork> Networks => this.Settings.NewsNetworks.Select(NewsNetwork.Find).OfType<NewsNetwork>().ToList();

    /// <summary>A chip per chosen category that at least one chosen network covers.</summary>
    public override IReadOnlyList<FeedChip> Chips
    {
        get
        {
            var networks = this.Networks;
            var chosen = this.Settings.NewsCategories;
            return NewsCategory.All
                .Where(c => chosen.Contains(c.Key) && networks.Any(n => n.Feeds.ContainsKey(c.Key)))
                .Select(c => new FeedChip(c.Key, c.Name))
                .ToList();
        }
    }

    protected override IReadOnlyList<FeedSource> SourcesFor(string chip)
        => this.Networks
            .Where(n => n.Feeds.ContainsKey(chip))
            .Select(n => new FeedSource(n.Feeds[chip], n.Name, n.Color, n.TitleSuffix))
            .ToList();

    protected override string? StoredFilter
    {
        get => this.Settings.NewsFilter;
        set => this.Settings.NewsFilter = value;
    }

    public void SetNetwork(string key, bool on)
    {
        var networks = this.Settings.NewsNetworks.Where(n => n != key).ToList();
        if (on)
            networks.Add(key);
        this.Settings.NewsNetworks = NewsNetwork.All.Select(n => n.Key).Where(networks.Contains).ToList();
    }

    public void SetCategory(string key, bool on)
    {
        var categories = this.Settings.NewsCategories.Where(c => c != key).ToList();
        if (on)
            categories.Add(key);
        this.Settings.NewsCategories = NewsCategory.All.Select(c => c.Key).Where(categories.Contains).ToList();
    }
}

/// <summary>Any feeds you add: a chip for all of them together, then one per feed.</summary>
[Singleton]
public sealed class RssService : FeedListService
{
    public const string All = "all";

    readonly FeedService feeds;

    public RssService(AppSettings settings, FeedService feeds) : base(settings, feeds)
    {
        this.feeds = feeds;
        settings.Changed += (_, key) =>
        {
            if (key != nameof(AppSettings.RssFeeds))
                return;
            this.Raise();
            _ = this.RefreshAsync();
        };
    }

    public IReadOnlyList<RssFeed> Feeds => this.Settings.RssFeeds;

    /// <summary>All, then each feed - or none at all before the first feed is added.</summary>
    public override IReadOnlyList<FeedChip> Chips
    {
        get
        {
            var feeds = this.Feeds;
            if (feeds.Count == 0)
                return [];
            var chips = new List<FeedChip>();
            // With one feed, "All" would only repeat it.
            if (feeds.Count > 1)
                chips.Add(new FeedChip(All, "All"));
            chips.AddRange(feeds.Select(f => new FeedChip(f.Url, f.Title)));
            return chips;
        }
    }

    protected override IReadOnlyList<FeedSource> SourcesFor(string chip)
        => this.Feeds
            .Where(f => chip == All || f.Url == chip)
            .Select(f => new FeedSource(f.Url, f.Title))
            .ToList();

    protected override string? StoredFilter
    {
        get => this.Settings.RssFilter;
        set => this.Settings.RssFilter = value;
    }

    /// <summary>
    /// Adds the feed at what was typed - a feed's address or a site that names one. Null when added;
    /// otherwise why not, to show under the box.
    /// </summary>
    public async Task<string?> AddAsync(string typed)
    {
        var feed = await this.feeds.ProbeAsync(typed).ConfigureAwait(true);
        if (feed == null)
            return $"No feed found at {typed.Trim()}. Try the feed's own address.";
        if (this.Feeds.Any(f => f.Url == feed.Url))
            return $"{feed.Title} is already in your feeds.";
        this.Settings.RssFeeds = [.. this.Feeds, new RssFeed(feed.Url, feed.Title)];
        return null;
    }

    public void Remove(string url) => this.Settings.RssFeeds = this.Feeds.Where(f => f.Url != url).ToList();
}

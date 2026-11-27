using Microsoft.Maui.Controls.Shapes;
using Notcho.Services;

namespace Notcho.Views;

/// <summary>
/// The News and RSS tabs: chips across the top (categories for News, feeds for RSS), then the chosen
/// chip's stories, newest first - who it's from and how long ago, the headline, and a line of summary.
/// A click opens the story in the browser.
/// </summary>
sealed class FeedTab : VerticalStackLayout
{
    const double ListHeight = 330;
    /// <summary>Rows built before the tab shows; the rest follow a batch at a time (see ClipboardTab).</summary>
    const int FirstRows = 7;
    const int RowsPerBatch = 8;

    /// <param name="symbol">The tab's own symbol, for its empty state.</param>
    /// <param name="emptyText">Said before anything is set up in Settings.</param>
    public FeedTab(FeedListService service, string title, string symbol, Color tint, string emptyText, Action openSettings, TimeProvider time)
    {
        this.Spacing = 10;

        var chips = service.Chips;
        if (chips.Count == 0)
        {
            this.Children.Add(Ui.Card(new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new HorizontalStackLayout { Spacing = 8, Children = { Ui.Symbol(symbol, 16, tint), Ui.Label(title, 16, bold: true) } },
                    Ui.Label(emptyText, 12, color: Ui.Secondary),
                    new HorizontalStackLayout { Children = { Ui.Chip("Set up in Settings", openSettings, selected: true, symbol: "gearshape.fill") } }
                }
            }, new Thickness(14, 12)));
            return;
        }

        this.Children.Add(Header(service, chips, openSettings));

        var items = service.Items(out var loading, out var failed);
        if (items.Count == 0)
        {
            var message = loading ? "Fetching the latest stories…"
                : failed ? "Couldn't reach these feeds. They'll be tried again in a minute."
                : "No stories here right now.";
            this.Children.Add(Ui.Card(Ui.Label(message, 12, color: Ui.Secondary).Centered(), new Thickness(12, 24)));
            return;
        }

        var now = time.GetUtcNow();
        var rows = new VerticalStackLayout { Spacing = 2 };
        foreach (var (item, source) in items.Take(FirstRows))
            rows.Children.Add(Row(item, source, now));

        var rest = items.Skip(FirstRows).ToList();
        void AddRest(int from)
        {
            // Switched away before the list was all in.
            if (!this.IsLoaded)
                return;
            foreach (var (item, source) in rest.Skip(from).Take(RowsPerBatch))
                rows.Children.Add(Row(item, source, now));
            if (from + RowsPerBatch < rest.Count)
                this.Dispatcher.Dispatch(() => AddRest(from + RowsPerBatch));
        }
        void OnLoaded(object? sender, EventArgs e)
        {
            this.Loaded -= OnLoaded;
            this.Dispatcher.Dispatch(() => AddRest(0));
        }
        if (rest.Count > 0)
            this.Loaded += OnLoaded;

        this.Children.Add(Ui.Card(new ScrollView { Content = rows, HeightRequest = ListHeight }, new Thickness(6, 6)));
    }

    /// <summary>The chips, scrolling sideways when there are many, and the way to Settings after them.</summary>
    static View Header(FeedListService service, IReadOnlyList<FeedChip> chips, Action openSettings)
    {
        var filter = service.Filter;
        var strip = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        foreach (var chip in chips)
        {
            var key = chip.Key;
            strip.Children.Add(Ui.Chip(chip.Name, () => service.SetFilter(key), key == filter));
        }

        return new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                new ScrollView
                {
                    Orientation = ScrollOrientation.Horizontal,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                    Content = strip
                }.At(column: 0),
                Ui.SymbolButton("square.and.pencil", openSettings, 13, Ui.Secondary, "Choose in Settings", Ui.Raised, new Thickness(7, 5)).At(column: 1)
            }
        };
    }

    static View Row(FeedItem item, FeedSource source, DateTimeOffset now)
    {
        var meta = new HorizontalStackLayout
        {
            Spacing = 6,
            Children = { Ui.Label(source.Name, 10, bold: true, color: SourceColor(source)) }
        };
        if (item.Published is { } published)
            meta.Children.Add(Ui.Label(Age(now - published), 10, color: Ui.Tertiary));

        var headline = Ui.Label(item.Title, 13, bold: true);
        headline.MaxLines = 2;
        headline.LineBreakMode = LineBreakMode.WordWrap;

        var body = new VerticalStackLayout { Spacing = 2, Children = { meta, headline } };
        if (item.Summary != null)
            body.Children.Add(Ui.Label(item.Summary, 11, color: Ui.Secondary));

        var row = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(8, 6),
            Content = body
        }.Pressable(() => Open(item.Link));
        SemanticProperties.SetDescription(row, $"{item.Title}, {source.Name}. Opens in the browser.");
        return row;
    }

    static void Open(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
            _ = Launcher.OpenAsync(uri);
    }

    /// <summary>The network's colour, lifted where it's too dark to read on the notch's black.</summary>
    static Color SourceColor(FeedSource source)
    {
        if (source.Color == null)
            return Ui.Orange;
        var color = Color.FromArgb(source.Color);
        return color.GetLuminosity() < 0.6f ? color.WithLuminosity(0.68f) : color;
    }

    static string Age(TimeSpan ago) => ago switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => $"{(int)ago.TotalMinutes}m ago",
        { TotalDays: < 1 } => $"{(int)ago.TotalHours}h ago",
        _ => $"{(int)ago.TotalDays}d ago"
    };
}

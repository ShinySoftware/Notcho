using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace ShinyNotch.Views;

public enum NotchTab
{
    Media,
    Weather,
    Tray,
    Stats,
    Processes,
    Capture,
    Mirror,
    Notes,
    Tasks,
    Clipboard,
    Calendar,
    Timer,
    Calculator,
    Prompter
}

/// <summary>A tab as the strip and Settings show it.</summary>
public sealed record NotchTabInfo(NotchTab Tab, string Symbol, string Name)
{
    /// <summary>Every tab, in strip order. Whether one shows depends on the platform and on Settings.</summary>
    public static readonly IReadOnlyList<NotchTabInfo> All =
    [
        new(NotchTab.Media, "music.note", "Now Playing"),
        new(NotchTab.Weather, "cloud.sun.fill", "Weather"),
        new(NotchTab.Tray, "tray.and.arrow.down.fill", "File Tray"),
        new(NotchTab.Stats, "cpu", "System"),
        new(NotchTab.Processes, "list.bullet.rectangle", "Processes"),
        new(NotchTab.Capture, "camera.viewfinder", "Capture"),
        new(NotchTab.Mirror, "person.crop.square.fill", "Mirror"),
        new(NotchTab.Notes, "waveform", "Voice Notes"),
        new(NotchTab.Tasks, "checklist", "Tasks"),
        new(NotchTab.Clipboard, "doc.on.clipboard", "Clipboard"),
        new(NotchTab.Calendar, "calendar", "Calendar"),
        new(NotchTab.Timer, "timer", "Timer"),
        new(NotchTab.Calculator, "plus.forwardslash.minus", "Calculator"),
        new(NotchTab.Prompter, "text.alignleft", "Teleprompter")
    ];
}

/// <summary>
/// The expanded notch. The collapsed notch stays in the band beside the camera exactly as it was - its
/// size and what it shows - and the panel hangs below it, as wide as it needs. So however wide the panel,
/// what's in the menu bar is no wider than the closed notch: either side of that neck the window is
/// see-through. The tab strip heads the panel (pin and settings close it), the selected tab's body follows,
/// and the <see cref="ControlsBar"/> closes every tab.
/// </summary>
sealed class PanelView : Grid, ILiveView, IDisposable
{
    const double TabSymbol = 16;
    const double CornerRadius = 22;
    /// <summary>The curves the neck's sides flare out on into the panel's top edge.</summary>
    const double FlareRadius = 12;

    readonly double width;
    // Concave: black in the corner between the neck's side and the panel's top, cut away by a quarter circle.
    readonly Path leftFlare = Flare($"M {FlareRadius},0 L {FlareRadius},{FlareRadius} L 0,{FlareRadius} A {FlareRadius},{FlareRadius} 0 0 0 {FlareRadius},0 Z", LayoutOptions.Start);
    readonly Path rightFlare = Flare($"M 0,0 A {FlareRadius},{FlareRadius} 0 0 0 {FlareRadius},{FlareRadius} L 0,{FlareRadius} Z", LayoutOptions.End);
    View neck;

    public View Body { get; }

    public ControlsBar Controls { get; }

    /// <param name="tabs">The tabs to show: the ones this platform has and Settings hasn't turned off.</param>
    /// <param name="neck">What the panel hangs from: the collapsed notch, or just the camera.</param>
    /// <param name="width">The panel's width: <see cref="Ui.PanelWidth"/>, unless the tab asks for more.</param>
    public PanelView(NotchTab selected, IReadOnlyList<NotchTabInfo> tabs, View neck, View body, ControlsBar controls, double width, bool pinned, Action<NotchTab> select, Action togglePin, Action openSettings)
    {
        this.Body = body;
        this.Controls = controls;
        this.width = width;
        this.neck = neck;
        this.RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];
        this.Children.Add(this.leftFlare.At(row: 0));
        this.Children.Add(this.rightFlare.At(row: 0));
        this.Fit();

        // Buttons share the room across the panel: the more there are, the less padding each gets. The
        // symbols stay full size.
        var count = tabs.Count + 2;
        var inset = Math.Clamp(Math.Floor(((width - Ui.PanelPadding) / count - Ui.SymbolBox(TabSymbol)) / 2), 1, 6);

        // The selected tab sits on a pill; the pin only changes colour, since it's a switch, not a place.
        // The others are secondary grey, not tertiary: on black, tertiary was too faint to find.
        View Tab(string symbol, Action tapped, bool on, string hint, Color? onColor = null, bool pill = true)
            => Ui.SymbolButton(symbol, tapped, TabSymbol, on ? onColor ?? Ui.Text : Ui.Secondary, hint, on && pill ? Ui.Raised : null, padding: new Thickness(inset, 4));

        // No spacing: up to sixteen buttons have to fit across.
        var left = new HorizontalStackLayout { Spacing = 0, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center };
        foreach (var t in tabs)
        {
            var tab = t.Tab;
            left.Children.Add(Tab(t.Symbol, () => select(tab), tab == selected, t.Name));
        }

        var right = new HorizontalStackLayout { Spacing = 0, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        right.Children.Add(Tab(pinned ? "pin.fill" : "pin", togglePin, pinned, pinned ? "Unpin" : "Keep open", Ui.Orange, pill: false));
        right.Children.Add(Tab("gearshape.fill", openSettings, false, "Settings"));

        var strip = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
        strip.Children.Add(left.At(column: 0));
        strip.Children.Add(right.At(column: 1));

        var card = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)],
            RowSpacing = 10,
            Padding = new Thickness(Ui.PanelPadding / 2, 8, Ui.PanelPadding / 2, 16)
        };
        card.Children.Add(strip.At(row: 0));
        card.Children.Add(body.At(row: 1));
        card.Children.Add(controls.At(row: 2));

        // The neck runs straight on into the top edge, so the top corners are the closed notch's.
        this.Children.Add(new Border
        {
            BackgroundColor = Colors.Black,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(PeekView.CornerRadius, PeekView.CornerRadius, CornerRadius, CornerRadius) },
            WidthRequest = width,
            HorizontalOptions = LayoutOptions.Center,
            Content = card
        }.At(row: 1));
    }

    /// <summary>
    /// The neck, swapped in place when what the closed notch shows has changed. The panel itself isn't
    /// rebuilt: the window is already as wide as the panel, so only the band changes.
    /// </summary>
    public View Neck
    {
        get => this.neck;
        set
        {
            this.Children.Remove(this.neck);
            this.neck = value;
            this.Fit();
        }
    }

    /// <summary>
    /// Centred on the camera, like the closed notch; the panel is as wide as the wider of it and the body.
    /// The flares sit either side of the neck's black, which a winged neck draws over only part of its width.
    /// </summary>
    void Fit()
    {
        this.neck.HorizontalOptions = LayoutOptions.Center;
        this.Children.Add(this.neck.At(row: 0));
        var wings = this.neck is IWinged winged ? winged.Wings : new Wings(0, 0, this.neck.WidthRequest);
        var whole = Math.Max(this.width, wings.Width);
        this.WidthRequest = whole;

        // From the window's edges to the black's, and to the panel's straight top edge (past its corners).
        var side = (whole - wings.Width) / 2;
        var card = (whole - this.width) / 2 + PeekView.CornerRadius;
        var leftRoom = side + wings.LeftInset - card;
        var rightRoom = side + wings.RightInset - card;
        this.leftFlare.IsVisible = leftRoom >= FlareRadius;
        this.leftFlare.Margin = new Thickness(side + wings.LeftInset - FlareRadius, 0, 0, 0);
        this.rightFlare.IsVisible = rightRoom >= FlareRadius;
        this.rightFlare.Margin = new Thickness(0, 0, side + wings.RightInset - FlareRadius, 0);
    }

    static Path Flare(string data, LayoutOptions side) => new()
    {
        Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(data)!,
        Fill = Colors.Black,
        StrokeThickness = 0,
        WidthRequest = FlareRadius,
        HeightRequest = FlareRadius,
        HorizontalOptions = side,
        VerticalOptions = LayoutOptions.End,
        InputTransparent = true
    };

    public void Tick()
    {
        (this.neck as ILiveView)?.Tick();
        (this.Body as ILiveView)?.Tick();
        this.Controls.Tick();
    }

    public void Dispose() => (this.Body as IDisposable)?.Dispose();
}

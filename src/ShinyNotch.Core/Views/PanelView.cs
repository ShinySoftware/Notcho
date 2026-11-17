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
    const double TabText = 12;
    const double TabTextGap = 6;
    const double CornerRadius = 22;
    /// <summary>The curves the neck's sides flare out on into the panel's top edge.</summary>
    const double FlareRadius = 12;

    readonly Action<NotchTab> select;
    readonly Action togglePin;
    readonly Action openSettings;
    // Concave: black in the corner between the neck's side and the panel's top, cut away by a quarter circle.
    readonly Path leftFlare = Flare($"M {FlareRadius},0 L {FlareRadius},{FlareRadius} L 0,{FlareRadius} A {FlareRadius},{FlareRadius} 0 0 0 {FlareRadius},0 Z", LayoutOptions.Start);
    readonly Path rightFlare = Flare($"M 0,0 A {FlareRadius},{FlareRadius} 0 0 0 {FlareRadius},{FlareRadius} L 0,{FlareRadius} Z", LayoutOptions.End);
    readonly Grid card = new()
    {
        RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)],
        RowSpacing = 10,
        Padding = new Thickness(Ui.PanelPadding / 2, 8, Ui.PanelPadding / 2, 16)
    };
    readonly Border surface;
    double width;
    View neck;
    View? strip;

    public View Body { get; private set; } = null!;

    public ControlsBar Controls { get; private set; } = null!;

    /// <param name="neck">What the panel hangs from: the collapsed notch, or just the camera.</param>
    public PanelView(View neck, Action<NotchTab> select, Action togglePin, Action openSettings)
    {
        this.neck = neck;
        this.select = select;
        this.togglePin = togglePin;
        this.openSettings = openSettings;
        this.RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];
        this.Children.Add(this.leftFlare.At(row: 0));
        this.Children.Add(this.rightFlare.At(row: 0));
        this.Children.Add(this.neck.At(row: 0));

        // The neck runs straight on into the top edge, so the top corners are the closed notch's.
        this.surface = new Border
        {
            BackgroundColor = Colors.Black,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(PeekView.CornerRadius, PeekView.CornerRadius, CornerRadius, CornerRadius) },
            HorizontalOptions = LayoutOptions.Center,
            Content = this.card
        };
        this.Children.Add(this.surface.At(row: 1));
    }

    /// <summary>
    /// Shows a tab, in place. Switching tabs swaps only the strip and the body (and the controls, when
    /// given): the notch around them stays, and reshapes to the new size by itself. Rebuilding the whole
    /// notch instead meant a new window page every time, which took a few hundred milliseconds.
    /// </summary>
    /// <param name="tabs">The tabs to show: the ones this platform has and Settings hasn't turned off.</param>
    /// <param name="width">The panel's width: <see cref="Ui.PanelWidth"/>, unless the tab asks for more.</param>
    /// <param name="controls">New controls, or null to keep the ones showing.</param>
    public void Show(NotchTab selected, IReadOnlyList<NotchTabInfo> tabs, View body, ControlsBar? controls, double width, bool pinned)
    {
        this.width = width;
        this.surface.WidthRequest = width;
        this.Fit();

        this.Replace(this.strip, this.strip = this.Strip(selected, tabs, width, pinned), 0);
        this.Replace(this.Body, this.Body = body, 1);
        if (controls != null)
            this.Replace(this.Controls, this.Controls = controls, 2);
    }

    void Replace(View? old, View now, int row)
    {
        if (old != null)
            this.card.Children.Remove(old);
        this.card.Children.Add(now.At(row: row));
    }

    View Strip(NotchTab selected, IReadOnlyList<NotchTabInfo> tabs, double width, bool pinned)
    {
        // Buttons share the room across the panel, less the selected tab's name: the more there are, the
        // less padding each gets. The symbols stay full size.
        var count = tabs.Count + 2;
        var name = tabs.FirstOrDefault(t => t.Tab == selected)?.Name;
        var nameWidth = name != null ? Math.Ceiling(Ui.Platform.TextWidth(name, TabText, bold: true)) + TabTextGap : 0;
        var inset = Math.Clamp(Math.Floor(((width - Ui.PanelPadding - nameWidth) / count - Ui.SymbolBox(TabSymbol)) / 2), 1, 6);

        // The pin only changes colour, since it's a switch, not a place. The others are secondary grey, not
        // tertiary: on black, tertiary was too faint to find.
        View Tab(string symbol, Action tapped, bool on, string hint, Color? onColor = null)
            => Ui.SymbolButton(symbol, tapped, TabSymbol, on ? onColor ?? Ui.Text : Ui.Secondary, hint, padding: new Thickness(inset, 4));

        // The selected tab sits on a pill with its name beside the symbol, so the strip says where you are.
        View Selected(NotchTabInfo t)
        {
            var label = Ui.Label(t.Name, TabText, bold: true);
            label.VerticalOptions = LayoutOptions.Center;
            var row = new HorizontalStackLayout { Spacing = TabTextGap, Children = { Ui.Symbol(t.Symbol, TabSymbol, Ui.Text), label } };
            var pill = new Border
            {
                AutomationId = t.Name,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                BackgroundColor = Ui.Raised,
                Padding = new Thickness(Math.Max(inset, 8), 4),
                VerticalOptions = LayoutOptions.Center,
                Content = row
            };
            return pill.Pressable(() => this.select(t.Tab));
        }

        // No spacing: up to sixteen buttons have to fit across.
        var left = new HorizontalStackLayout { Spacing = 0, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center };
        foreach (var t in tabs)
        {
            var tab = t.Tab;
            left.Children.Add(tab == selected ? Selected(t) : Tab(t.Symbol, () => this.select(tab), false, t.Name));
        }

        var right = new HorizontalStackLayout { Spacing = 0, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        right.Children.Add(Tab(pinned ? "pin.fill" : "pin", this.togglePin, pinned, pinned ? "Unpin" : "Keep open", Ui.Orange));
        right.Children.Add(Tab("gearshape.fill", this.openSettings, false, "Settings"));

        var strip = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
        strip.Children.Add(left.At(column: 0));
        strip.Children.Add(right.At(column: 1));
        return strip;
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
            this.Children.Add(this.neck.At(row: 0));
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

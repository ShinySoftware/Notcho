using Microsoft.Maui.Controls.Shapes;
using Notcho.Platform;
using Notcho.Services;

namespace Notcho.Views;

/// <summary>Something the closed notch can show, turned on or off in Settings.</summary>
public enum PeekItem
{
    Music,
    Weather,
    Cpu,
    Gpu,
    Memory,
    NetworkIn,
    NetworkOut,
    DiskRead,
    DiskWrite
}

/// <summary>A music control on the closed notch, turned on or off in Settings (click to open only).</summary>
public enum PeekMusicButton
{
    Previous,
    PlayPause,
    Next
}

/// <summary>A closed-notch music button as Settings and the notch show it.</summary>
public sealed record PeekMusicButtonInfo(PeekMusicButton Button, string Name, string Symbol)
{
    /// <summary>Every button, in Settings' order and the notch's.</summary>
    public static readonly IReadOnlyList<PeekMusicButtonInfo> All =
    [
        new(PeekMusicButton.Previous, "Previous", "backward.fill"),
        new(PeekMusicButton.PlayPause, "Play / pause", "playpause.fill"),
        new(PeekMusicButton.Next, "Next", "forward.fill")
    ];

    /// <summary>The buttons turned on, from the names Settings keeps. A name no longer known is skipped.</summary>
    public static IReadOnlySet<PeekMusicButton> Shown(IEnumerable<string> names)
        => names.Select(n => Enum.TryParse<PeekMusicButton>(n, out var button) ? button : (PeekMusicButton?)null).OfType<PeekMusicButton>().ToHashSet();
}

/// <summary>A closed-notch item as Settings and the notch show it.</summary>
/// <param name="Tag">
/// The short name a percentage goes by in the notch ("CPU 23%"). Null for a rate, which goes by its symbol.
/// </param>
public sealed record PeekItemInfo(PeekItem Item, string Name, string Symbol, Color Color, string? Tag = null)
{
    /// <summary>A system reading, sampled every second while it shows.</summary>
    public bool IsReading => this.Item is not (PeekItem.Music or PeekItem.Weather);

    /// <summary>Every item, in Settings' order - and, for the readings, the notch's.</summary>
    public static readonly IReadOnlyList<PeekItemInfo> All =
    [
        new(PeekItem.Music, "What's playing", "music.note", Ui.Red),
        new(PeekItem.Weather, "Weather", "cloud.sun.fill", Ui.Blue),
        new(PeekItem.Cpu, "CPU load", "cpu", Ui.Teal, "CPU"),
        new(PeekItem.Gpu, "GPU load", "cpu", Ui.Purple, "GPU"),
        new(PeekItem.Memory, "Memory used", "memorychip", Ui.Green, "MEM"),
        new(PeekItem.NetworkIn, "Network in", "arrow.down.circle.fill", Ui.Blue),
        new(PeekItem.NetworkOut, "Network out", "arrow.up.circle.fill", Ui.Pink),
        new(PeekItem.DiskRead, "Disk reads", "arrow.down.doc.fill", Ui.Orange),
        new(PeekItem.DiskWrite, "Disk writes", "arrow.up.doc.fill", Ui.Yellow)
    ];

    /// <summary>The items turned on, from the names Settings keeps. A name no longer known is skipped.</summary>
    public static IReadOnlySet<PeekItem> Shown(IEnumerable<string> names)
        => names.Select(n => Enum.TryParse<PeekItem>(n, out var item) ? item : (PeekItem?)null).OfType<PeekItem>().ToHashSet();
}

/// <summary>
/// Where a winged view (<see cref="PeekView.Lay"/>) draws its black: how far in from each side of the window
/// it starts, and the window's whole width.
/// </summary>
public readonly record struct Wings(double LeftInset, double RightInset, double Width);

/// <summary>Content laid out as wings either side of the camera, black over only the width each wants.</summary>
interface IWinged
{
    Wings Wings { get; }
}

/// <summary>
/// The collapsed notch: a "wing" either side of the camera, at the camera's height. In order from the left
/// edge round to the right: what's playing (the artwork and its bars, in the artwork's colour), any music
/// buttons asked for, any system readings turned on in Settings, a cup while keeping awake, and last the
/// countdown, a meeting that's about to start, or the weather. They're split across the camera so the two wings come out as even as
/// they can (see <see cref="Balance"/>).
/// </summary>
/// <remarks>
/// Each wing is the width asked for in Settings or what its content needs, whichever is more - so a wing
/// asked for at 0 with nothing in it is just the camera. With the widths locked, both wings take the
/// wider of the two, keeping the camera centred.
/// <para>
/// The notch window is always centred on the camera, so uneven wings can't come from the window's width.
/// Instead the window is as wide as the wider wing on both sides, the notch's own surface is clear (see
/// <c>ConfigurePeek</c>), and the black is drawn here, over just the wings asked for.
/// </para>
/// </remarks>
sealed class PeekView : Grid, ILiveView, IWinged
{
    readonly CountdownService countdown;
    readonly Border black;
    readonly CalendarService calendar;
    readonly WeatherService weather;
    readonly TimeProvider time;
    readonly SystemStatsService stats;
    readonly bool showWeather;
    readonly Label? right;
    readonly IReadOnlyList<(PeekItem Item, Label Value)> readings;

    const double ArtworkSize = 22;
    const double BarsWidth = 16;
    const double WingSpacing = 8;
    const double RightSpacing = 4;
    const double RightTextSize = 12;
    const double ReadingTagSize = 9;
    const double ReadingSymbolSize = 10;
    const double ReadingSpacing = 3;
    const double MusicButtonSize = 11;
    const double AwakeSymbolSize = 12;
    static readonly Thickness MusicButtonPadding = new(5, 4);
    /// <summary>Kept clear between a wing's content and the camera.</summary>
    const double CameraGap = 6;
    /// <summary>Between a wing's content and the notch's outer edge (the notch itself has no padding).</summary>
    public const double EdgeGap = 12;
    /// <summary>The black's bottom corners.</summary>
    public const double CornerRadius = 12;

    PeekView(double cutoutWidth, double leftWidth, double rightWidth, double minWidth, double height, View? left, View? right, Label? rightText, IReadOnlyList<(PeekItem, Label)> readings, bool showWeather, CountdownService countdown, CalendarService calendar, WeatherService weather, SystemStatsService stats, TimeProvider time)
    {
        this.countdown = countdown;
        this.calendar = calendar;
        this.weather = weather;
        this.stats = stats;
        this.time = time;
        this.showWeather = showWeather;
        this.right = rightText;
        this.readings = readings;

        this.HeightRequest = height;
        this.Wings = Lay(this, cutoutWidth, leftWidth, rightWidth, minWidth, out this.black);
        var (leftInset, rightInset, _) = this.Wings;

        if (left != null)
        {
            left.Margin = new Thickness(leftInset + EdgeGap, 0, 0, 0);
            this.Children.Add(left.At(column: 0));
        }
        if (right != null)
        {
            right.Margin = new Thickness(0, 0, rightInset + EdgeGap, 0);
            this.Children.Add(right.At(column: 2));
        }
    }

    public Wings Wings { get; }

    /// <summary>Hung above the open panel (see <see cref="PanelView"/>): the black runs on into it, so it has no bottom corners.</summary>
    public void JoinPanel()
    {
        this.black.StrokeShape = new RoundRectangle { CornerRadius = 0 };
        Ui.Paint(this.black, Ui.OpenFill.TopOnly());
    }

    /// <summary>
    /// Lays out a grid of wings either side of the camera: both columns as wide as the wider wing, so the
    /// window stays centred on the camera, and the notch's black drawn over only the width each wing wants.
    /// Returns where the black is: how far in from each column's outer edge its wing's black starts (where
    /// content should begin), and the whole width.
    /// </summary>
    /// <param name="minWidth">
    /// The narrowest the black may be. Any shortfall widens the middle, where the camera is (or would be),
    /// so the wings' content stays at the outer edges.
    /// </param>
    /// <param name="black">The black, for a caller that reshapes it later.</param>
    public static Wings Lay(Grid grid, double cutoutWidth, double leftWidth, double rightWidth, double minWidth, out Border black)
    {
        var wing = Math.Max(leftWidth, rightWidth);
        var middle = Math.Max(Math.Max(cutoutWidth, 8), minWidth - leftWidth - rightWidth);
        grid.ColumnDefinitions =
        [
            new ColumnDefinition(wing),
            new ColumnDefinition(middle),
            new ColumnDefinition(wing)
        ];

        var leftInset = wing - leftWidth;
        var rightInset = wing - rightWidth;
        black = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 0, CornerRadius, CornerRadius) },
            Margin = new Thickness(leftInset, 0, rightInset, 0),
            InputTransparent = true
        };
        Ui.Paint(black, Ui.ClosedFill);
        grid.Children.Add(black.At(columnSpan: 3));
        return new Wings(leftInset, rightInset, wing + middle + wing);
    }

    /// <summary>Shows system readings, so it wants ticking every second.</summary>
    public bool ShowsReadings => this.readings.Count > 0;

    /// <summary>
    /// Null when there is nothing worth peeking at, so the notch can disappear into the camera - unless
    /// <paramref name="preview"/>, when an empty one is shown: so a size being changed in Settings can be seen,
    /// or on a display without a camera notch, where the black stand-in is all there is to hover.
    /// </summary>
    /// <param name="minWidth">The narrowest the notch may be (see <see cref="Lay"/>); 0 around a real camera.</param>
    /// <param name="buttons">The music buttons to show while there's a track; empty unless the notch opens on a click.</param>
    /// <param name="pressed">A music button was clicked, before it acts - so the notch can tell it from a click on itself.</param>
    /// <param name="awake">Keeping awake, and Settings wants the cup that says so.</param>
    public static PeekView? Create(double cutoutWidth, double leftWish, double rightWish, bool locked, double minWidth, double height, bool preview, IReadOnlySet<PeekItem> items, IReadOnlySet<PeekMusicButton> buttons, Action pressed, bool awake, INowPlayingService media, CountdownService countdown, CalendarService calendar, WeatherService weather, SystemStatsService stats, TimeProvider time)
    {
        // Every part with its width, in order from the left wing's outer edge round to the right wing's.
        var parts = new List<(View View, double Width)>();
        void Add(View part, double width) => parts.Add((part, width));

        if (items.Contains(PeekItem.Music) && media.Current is { } now)
        {
            // The artwork and its bars are one part: they shouldn't be split across the camera.
            var music = new HorizontalStackLayout
            {
                Spacing = WingSpacing,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    Ui.Artwork(media.Artwork, ArtworkSize, 6),
                    MusicBars.Create(now.TrackKey, now.IsPlaying, media.ArtworkColor ?? Ui.Text, BarsWidth, 14)
                }
            };
            Add(music, ArtworkSize + WingSpacing + BarsWidth);
        }

        if (buttons.Count > 0 && media.Current is { } track)
        {
            var row = new HorizontalStackLayout { VerticalOptions = LayoutOptions.Center };
            foreach (var info in PeekMusicButtonInfo.All.Where(b => buttons.Contains(b.Button)))
                row.Add(MusicButton(info.Button, track.IsPlaying, media, pressed));
            Add(row, row.Count * (Ui.SymbolBox(MusicButtonSize) + MusicButtonPadding.HorizontalThickness));
        }

        var readings = new List<(PeekItem, Label)>();
        var shownReadings = PeekItemInfo.All.Where(i => i.IsReading && items.Contains(i.Item)).ToList();
        if (shownReadings.Count > 0)
        {
            // Carries on from the Stats tab or the last peek when that was a moment ago; else "—" for a second.
            stats.Begin();
            foreach (var info in shownReadings)
            {
                Add(Reading(info, stats.Current, out var value, out var width), width);
                readings.Add((info.Item, value));
            }
        }

        if (awake)
        {
            var cup = Ui.Symbol("cup.and.saucer.fill", AwakeSymbolSize, Ui.Yellow);
            cup.VerticalOptions = LayoutOptions.Center;
            Add(cup, Ui.SymbolBox(AwakeSymbolSize));
        }

        // The countdown, a meeting or the weather always ends the right wing, at its outer edge.
        var showWeather = items.Contains(PeekItem.Weather);
        Label? rightText = null;
        if (Right(countdown, calendar, weather, showWeather, time) is { } r)
        {
            rightText = Ui.Label(r.Text, RightTextSize, bold: true, color: r.Color);
            var status = new HorizontalStackLayout
            {
                Spacing = RightSpacing,
                VerticalOptions = LayoutOptions.Center,
                Children = { r.Multicolor ? Ui.Symbol(r.Symbol, 12, multicolor: true) : Ui.Symbol(r.Symbol, 11, r.Color), rightText }
            };
            Add(status, RightTextSize + RightSpacing + TextWidth(r.Text));
        }

        if (parts.Count == 0 && !preview)
            return null;

        var split = Balance(parts.Select(p => p.Width).ToList(), rightText != null);
        var left = Wing(parts.Take(split).ToList(), LayoutOptions.Start, out var leftContent);
        var right = Wing(parts.Skip(split).ToList(), LayoutOptions.End, out var rightContent);
        var leftNeeds = left != null ? Math.Ceiling(EdgeGap + leftContent + CameraGap) : 0;
        var rightNeeds = right != null ? Math.Ceiling(CameraGap + rightContent + EdgeGap) : 0;

        var (leftWidth, rightWidth) = Widths(leftWish, rightWish, locked, leftNeeds, rightNeeds);
        return new PeekView(cutoutWidth, leftWidth, rightWidth, minWidth, height, left, right, rightText, readings, showWeather, countdown, calendar, weather, stats, time);
    }

    /// <summary>
    /// How many of the parts go in the left wing, the rest going right: whichever split leaves the wider wing
    /// narrowest, so neither side is crowded while the other sits empty. A tie keeps more on the left, where
    /// the parts start.
    /// </summary>
    /// <param name="keepLast">The last part (the countdown, a meeting or the weather) stays on the right.</param>
    public static int Balance(IReadOnlyList<double> widths, bool keepLast)
    {
        double Side(int from, int to) => to <= from ? 0 : widths.Skip(from).Take(to - from).Sum() + WingSpacing * (to - from - 1);

        var most = keepLast ? widths.Count - 1 : widths.Count;
        var best = most;
        var bestWidest = double.MaxValue;
        for (var split = most; split >= 0; split--)
        {
            var widest = Math.Max(Side(0, split), Side(split, widths.Count));
            if (widest < bestWidest)
            {
                best = split;
                bestWidest = widest;
            }
        }
        return best;
    }

    /// <summary>A wing's parts in a row, or null with none; <paramref name="width"/> is what they take up.</summary>
    static HorizontalStackLayout? Wing(IReadOnlyList<(View View, double Width)> parts, LayoutOptions align, out double width)
    {
        width = parts.Sum(p => p.Width) + WingSpacing * Math.Max(0, parts.Count - 1);
        if (parts.Count == 0)
            return null;

        var row = new HorizontalStackLayout { Spacing = WingSpacing, VerticalOptions = LayoutOptions.Center, HorizontalOptions = align };
        foreach (var part in parts)
            row.Add(part.View);
        return row;
    }

    /// <summary>One music button. Play/pause shows what a click would do; the peek is rebuilt when that changes.</summary>
    static Border MusicButton(PeekMusicButton button, bool playing, INowPlayingService media, Action pressed)
    {
        var (symbol, hint, act) = button switch
        {
            PeekMusicButton.Previous => ("backward.fill", "Previous", (Action)media.Previous),
            PeekMusicButton.Next => ("forward.fill", "Next", media.Next),
            _ => (playing ? "pause.fill" : "play.fill", "Play or pause", media.PlayPause)
        };
        return Ui.SymbolButton(symbol, () =>
        {
            pressed();
            act();
        }, MusicButtonSize, Ui.Text, hint, padding: MusicButtonPadding);
    }

    /// <summary>
    /// A reading: its tag ("CPU") or symbol, then the value. The value is as wide as the widest it can be,
    /// so ticking in place (which doesn't rebuild the wing) never outgrows the room it was given.
    /// </summary>
    static View Reading(PeekItemInfo info, SystemStats? current, out Label value, out double width)
    {
        View label;
        double labelWidth;
        if (info.Tag is { } tag)
        {
            labelWidth = Math.Ceiling(Ui.Platform.TextWidth(tag, ReadingTagSize, bold: true));
            label = Ui.Label(tag, ReadingTagSize, bold: true, color: info.Color, width: labelWidth);
        }
        else
        {
            labelWidth = Ui.SymbolBox(ReadingSymbolSize);
            label = Ui.Symbol(info.Symbol, ReadingSymbolSize, info.Color);
        }

        var widest = info.Tag != null ? "888%" : SystemStatsService.CompactWidest;
        var valueWidth = Math.Ceiling(Ui.Platform.TextWidth(widest, RightTextSize, bold: true));
        value = Ui.Label(Value(info.Item, current), RightTextSize, bold: true, width: valueWidth);
        value.HorizontalTextAlignment = TextAlignment.End;

        width = labelWidth + ReadingSpacing + valueWidth;
        return new HorizontalStackLayout { Spacing = ReadingSpacing, VerticalOptions = LayoutOptions.Center, Children = { label, value } };
    }

    static string Value(PeekItem item, SystemStats? s)
    {
        static string Percent(double? percent) => percent is { } p ? $"{Math.Clamp(p, 0, 100):0}%" : "—";

        if (s == null)
            return "—";

        return item switch
        {
            PeekItem.Cpu => Percent(s.CpuPercent),
            PeekItem.Gpu => Percent(s.GpuPercent),
            PeekItem.Memory => Percent(s.MemoryPercent),
            PeekItem.NetworkIn => SystemStatsService.Compact(s.NetInPerSec),
            PeekItem.NetworkOut => SystemStatsService.Compact(s.NetOutPerSec),
            PeekItem.DiskRead => SystemStatsService.Compact(s.DiskReadPerSec),
            PeekItem.DiskWrite => SystemStatsService.Compact(s.DiskWritePerSec),
            _ => ""
        };
    }

    /// <summary>The wings' widths: each the wish or its content's need, whichever is more; locked, the wider for both.</summary>
    public static (double Left, double Right) Widths(double leftWish, double rightWish, bool locked, double leftNeeds, double rightNeeds)
    {
        var left = Math.Max(leftWish, leftNeeds);
        var right = Math.Max(rightWish, rightNeeds);
        return locked ? (Math.Max(left, right), Math.Max(left, right)) : (left, right);
    }

    /// <summary>
    /// The right wing's text as the platform will draw it. Every digit is measured as an 8, the widest, so a
    /// countdown ticking in place (which doesn't rebuild the wing) never outgrows the width it was given.
    /// </summary>
    static double TextWidth(string text)
    {
        var widest = new string(text.Select(c => char.IsAsciiDigit(c) ? '8' : c).ToArray());
        return Ui.Platform.TextWidth(widest, RightTextSize, bold: true);
    }

    static (string Symbol, bool Multicolor, string Text, Color Color)? Right(CountdownService countdown, CalendarService calendar, WeatherService weather, bool showWeather, TimeProvider time)
    {
        if (countdown.IsActive)
            return (countdown.IsPaused ? "pause.fill" : "timer", false, CountdownService.Format(countdown.Remaining), countdown.IsPaused ? Ui.Secondary : Ui.Orange);

        if (calendar.StartingWithin(TimeSpan.FromMinutes(15)) is { } next)
            return ("calendar", false, $"in {Math.Max(1, (int)Math.Ceiling((next.Start - time.GetLocalNow().DateTime).TotalMinutes))}m", next.Color);

        // Nothing more urgent: the weather where you are, the way the iPhone's island idles.
        if (showWeather && weather.Selected is { } report)
            return (WeatherService.Symbol(report.Code, report.IsDay), true, WeatherService.Degrees(report.Temperature), Ui.Text);

        return null;
    }

    public void Tick()
    {
        if (this.right != null && Right(this.countdown, this.calendar, this.weather, this.showWeather, this.time) is { } r)
            this.right.Text = r.Text;

        if (this.readings.Count > 0)
        {
            var s = this.stats.Sample();
            foreach (var (item, value) in this.readings)
                value.Text = Value(item, s);
        }
    }
}

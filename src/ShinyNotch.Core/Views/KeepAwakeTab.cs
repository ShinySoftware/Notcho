using Microsoft.Maui.Controls.Shapes;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// A ring with a cup in it, the switch for keeping awake until it's stopped: full while on, draining as a
/// timed session runs. Session lengths, more time, stop and whether the screen stays on sit beside it.
/// </summary>
sealed class KeepAwakeTab : Grid, ILiveView
{
    const double RingSize = 124;
    static readonly TimeSpan[] Presets = [TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(4)];
    static readonly TimeSpan More = TimeSpan.FromMinutes(15);

    readonly KeepAwakeService awake;
    readonly RingDrawable ring = new() { Fill = Ui.Yellow, Thickness = 8 };
    readonly GraphicsView ringView;
    readonly Label remaining;
    readonly Label caption;

    public KeepAwakeTab(KeepAwakeService awake, AppSettings settings)
    {
        this.awake = awake;
        this.ColumnSpacing = 16;
        this.ColumnDefinitions = [new ColumnDefinition(RingSize), new ColumnDefinition(GridLength.Star)];

        this.ringView = Drawing.View(this.ring, RingSize, RingSize);
        this.remaining = Ui.Label("", 22, bold: true).Centered();
        this.caption = Ui.Caption("").Centered();
        var cup = Ui.Symbol("cup.and.saucer.fill", 22, awake.IsActive ? Ui.Yellow : Ui.Tertiary);
        cup.HorizontalOptions = LayoutOptions.Center;
        var dial = new Border
        {
            AutomationId = "KeepAwakeToggle",
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = RingSize / 2 },
            BackgroundColor = Colors.Transparent,
            WidthRequest = RingSize,
            HeightRequest = RingSize,
            Content = new Grid
            {
                Children =
                {
                    this.ringView,
                    new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { cup, this.remaining, this.caption } }
                }
            }
        }.Pressable(awake.Toggle);
        this.Children.Add(dial.At(column: 0));

        var presets = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)],
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)]
        };
        presets.Children.Add(Ui.Chip("Until I stop", () => awake.Start(null), selected: awake.IsIndefinite, tint: Ui.Yellow, symbol: "infinity").At(row: 0, column: 0));
        for (var i = 0; i < Presets.Length; i++)
        {
            var length = Presets[i];
            var cell = i + 1;
            presets.Children.Add(Ui.Chip(KeepAwakeService.Name(length), () => awake.Start(length), selected: awake.Duration == length && awake.IsActive, tint: Ui.Yellow).At(row: cell / 3, column: cell % 3));
        }

        var options = new HorizontalStackLayout { Spacing = 8 };
        if (awake.IsActive && !awake.IsIndefinite)
            options.Children.Add(Ui.Chip(KeepAwakeService.Name(More), () => awake.Add(More), symbol: "plus"));
        if (awake.IsActive)
            options.Children.Add(Ui.Chip("Stop", awake.Stop, tint: Ui.Red, symbol: "xmark"));
        options.Children.Add(Ui.Chip("Screen stays on", () => settings.AwakeKeepDisplayOn = !settings.AwakeKeepDisplayOn, selected: settings.AwakeKeepDisplayOn, tint: Ui.Yellow, symbol: "display"));

        var side = new VerticalStackLayout
        {
            Spacing = 12,
            VerticalOptions = LayoutOptions.Center,
            Children = { Ui.Caption("Keep this computer awake"), presets, options }
        };
        this.Children.Add(side.At(column: 1));

        this.Tick();
    }

    public void Tick()
    {
        var on = this.awake.IsActive;
        this.remaining.Text = !on ? "Off" : this.awake.IsIndefinite ? "On" : CountdownService.Format(this.awake.Remaining);
        this.remaining.TextColor = on ? Ui.Text : Ui.Tertiary;
        this.caption.Text = !on ? "CLICK TO START"
            : this.awake.EndsAt is { } ends ? $"UNTIL {ends:t}".ToUpperInvariant()
            : "UNTIL YOU STOP";

        this.ring.Value = this.awake.Fraction;
        this.ringView.Invalidate();
    }
}

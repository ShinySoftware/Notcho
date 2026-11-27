using Shiny;
using Notcho.Platform;
using Notcho.Services;
using Notcho.Views;
using ButtonCell = Shiny.Maui.Controls.Cells.ButtonCell;
using CommandCell = Shiny.Maui.Controls.Cells.CommandCell;
using CellBase = Shiny.Maui.Controls.Cells.CellBase;
using CustomCell = Shiny.Maui.Controls.Cells.CustomCell;
using EntryCell = Shiny.Maui.Controls.Cells.EntryCell;
using LabelCell = Shiny.Maui.Controls.Cells.LabelCell;
using RadioCell = Shiny.Maui.Controls.Cells.RadioCell;
using SwitchCell = Shiny.Maui.Controls.Cells.SwitchCell;
using TableSection = Shiny.Maui.Controls.Sections.TableSection;
using Slider = Shiny.Maui.Controls.Slider;
using TableView = Shiny.Maui.Controls.TableView;

namespace Notcho;

/// <summary>
/// The one ordinary window: things that need typing (the teleprompter script) or a decision you make once.
/// The notch window can never take keyboard focus, so text entry lives here.
/// </summary>
/// <remarks>
/// A grouped <see cref="TableView"/>, one section per area, styled with the notch's own dark palette.
/// Only cells that edit in place: the picker and number cells open a dialog, and dialogs don't paint on
/// the AppKit head.
/// </remarks>
[Transient]
public sealed class SettingsPage : ContentPage
{
    // Kept across rebuilds of the page (see Rebuild): the AppKit head never realises rows added to a page
    // that is already on screen, so a changed list is a fresh page, and the search must survive that.
    static string searchText = "";
    static IReadOnlyList<WeatherPlace> searchResults = [];
    /// <summary>The category showing; kept across rebuilds, so a change in it doesn't jump to another.</summary>
    static SettingsCategory category = SettingsCategory.General;
    /// <summary>What the RSS box last typed, and why it couldn't be added; null when it was.</summary>
    static string rssText = "";
    static string? rssError;
    /// <summary>What the last backup or restore did, said under the Backup section's buttons.</summary>
    static string? backupStatus;
    /// <summary>Where the page being replaced was scrolled to, for the fresh one to carry on from; null once used.</summary>
    static double? keptScroll;

    /// <summary>Saves a script edit still waiting on its debounce.</summary>
    Action? flushScript;

    readonly UpdateService updates;
    readonly Notches notches;
    /// <summary>The release the Update section was built for, so only a different one rebuilds the page.</summary>
    readonly string? shownUpdate;
    /// <summary>The page's table, under the header: what Rebuild reads the scroll position from.</summary>
    readonly TableView table;

    public SettingsPage(AppSettings settings, CalendarService calendar, WeatherService weather, SportsService sports, NewsService news, RssService rss, Notches notch, IPlatformShell shell, IAppStartupInstaller startup, UpdateService updates, TeleprompterService prompter, CalculatorService calculator, IFileDialogs dialogs, TimeProvider time)
    {
        this.updates = updates;
        this.notches = notch;
        this.shownUpdate = updates.Available?.Version;
        this.Title = "Notcho";
        this.BackgroundColor = Ui.Surface;

        var table = this.table = new TableView
        {
            BackgroundColor = Ui.Surface,
            CellBackgroundColor = Ui.Raised,
            CellSelectedColor = Ui.Separator,
            CellTitleColor = Ui.Text,
            CellDescriptionColor = Ui.Secondary,
            CellValueTextColor = Ui.Secondary,
            CellHintTextColor = Ui.Secondary,
            CellAccentColor = Ui.Blue,
            CellIconSize = 18,
            HeaderBackgroundColor = Ui.Surface,
            FooterBackgroundColor = Ui.Surface,
            HeaderTextColor = Ui.Secondary,
            HeaderFontSize = 12,
            FooterTextColor = Ui.Tertiary,
            FooterFontSize = 11,
            SeparatorColor = Ui.Separator,
            SectionSeparatorHeight = 18,
            SectionSeparatorColor = Ui.Surface
        };

        // The inset goes inside the scroller, not around it: the rows scroll right to the window's edges
        // (and under its title bar), and the scroll bar runs down the window's own edge. The table has no
        // inset of its own, so it goes on the stack of rows it scrolls.
        if (table.Content is ScrollView { Content: Layout rows })
            rows.Padding = new Thickness(16, 0, 16, 16);

        // Only the chosen category's sections: the whole lot made one very long page.
        var sections = table.Root.Sections;
        switch (category)
        {
            case SettingsCategory.General:
                // A pending update comes first.
                if (UpdateSection(updates) is { } update)
                    sections.Add(update);
                sections.Add(GeneralSection(startup, shell));
                if (calendar.IsSupported)
                    sections.Add(this.PermissionsSection(calendar));
                sections.Add(this.BackupSection(settings, dialogs, time));
                break;
            case SettingsCategory.Notch:
                // Only with a choice to make: a second display connected.
                if (DisplaysSection(settings, notch) is { } displays)
                    sections.Add(displays);
                // Only where there's a display to move it on: one without a camera notch.
                if (PositionSection(settings, notch) is { } position)
                    sections.Add(position);
                sections.Add(this.NotchSection(settings));
                sections.Add(this.FillSection(settings, closed: true));
                sections.Add(this.FillSection(settings, closed: false));
                sections.Add(StandInSection(settings));
                sections.Add(PeekSection(settings));
                break;
            case SettingsCategory.Tabs:
                sections.Add(TabsSection(settings, notch.AvailableTabs));
                sections.Add(this.TeleprompterSection(settings, prompter));
                sections.Add(CalculatorSection(settings, calculator));
                sections.Add(KeepAwakeSection(settings));
                break;
            case SettingsCategory.Weather:
                // Nothing to ask for where the platform can't locate: the places are the whole story.
                if (!weather.IsLocationUnavailable)
                    sections.Add(this.WeatherSection(settings, weather));
                sections.Add(this.TemperatureSection(settings, weather));
                sections.Add(this.PlacesSection(settings, weather));
                break;
            case SettingsCategory.Sports:
                // Teams first, so the team box is near the top of the window (see TeamsSection).
                sections.Add(this.TeamsSection(sports));
                sections.Add(LeaguesSection(sports));
                break;
            case SettingsCategory.News:
                sections.Add(NewsNetworksSection(settings, news));
                sections.Add(NewsCategoriesSection(settings, news));
                sections.Add(this.RssSection(rss));
                break;
            default:
                sections.Add(AboutSection(shell, updates));
                break;
        }

        // The header stays put; the table under it is the page's one scroller.
        this.Content = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
            Children =
            {
                this.Header(updates.Available != null),
                table.At(row: 1)
            }
        };
        // The table renders its rows on the dispatcher, after the page has loaded (the window is made
        // hidden at startup). The AppKit head sizes the scroller from the first layout and doesn't grow it
        // when the rows arrive, so everything past the first few sections was cut off. Measuring again
        // once they're in, table and page both, fixes the scroll height.
        table.ModelChanged += (_, _) => this.Dispatcher.Dispatch(() =>
        {
            ((IView)table).InvalidateMeasure();
            ((IView)this).InvalidateMeasure();
            // A page built again (see Rebuild) stays where the old one was, once its rows are in.
            if (keptScroll is { } y && table.Content is ScrollView scroller)
            {
                keptScroll = null;
                this.Dispatcher.Dispatch(() => _ = scroller.ScrollToAsync(0, y, false));
            }
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Sections
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A switch for each connected display, with more than one: the notch can be on any of them, and on
    /// more than one at once. Null with only one display. The last one on can't be turned off.
    /// </summary>
    static TableSection? DisplaysSection(AppSettings settings, Notches notches)
    {
        var displays = notches.Displays;
        if (displays.Count < 2)
            return null;

        var section = Section("Displays", "The displays the notch shows on. Each opens and closes on its own; a new song, a finished countdown or the volume keys show on all of them. A display with a camera notch gets the notch around its camera.");
        foreach (var display in displays)
        {
            var on = Notches.Chosen(displays, settings.NotchDisplays).Any(d => d.Id == display.Id);
            var cell = NewSwitchCell(display.Name, Ui.SymbolSource(display.HasHardwareNotch ? "laptopcomputer" : "display", 16, Ui.Blue), on);
            if (display.IsPrimary)
                cell.Description = "Main display";
            cell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != SwitchCell.OnProperty.PropertyName)
                    return;

                var current = notches.Displays;
                var chosen = Notches.Chosen(current, settings.NotchDisplays).Select(d => d.Id).Where(id => id != display.Id).ToList();
                if (cell.On)
                {
                    chosen.Add(display.Id);
                }
                else if (chosen.Count == 0)
                {
                    // After the click: set now, AppKit finishes the click after and leaves it drawn off.
                    cell.Dispatcher.Dispatch(() => cell.On = true);
                    return;
                }

                // Picked displays that aren't plugged in now stay picked, for when they are again.
                var unplugged = settings.NotchDisplays.Where(id => current.All(d => d.Id != id));
                settings.NotchDisplays = unplugged.Concat(chosen).ToList();
            };
            section.Cells.Add(cell);
        }
        return section;
    }

    /// <summary>
    /// Lock or unlock where the notch sits, and put it back in the middle. Null when every display has a
    /// camera notch, which the notch always stays around.
    /// </summary>
    static TableSection? PositionSection(AppSettings settings, Notches notches)
    {
        var displays = notches.Displays;
        if (displays.Count > 0 && displays.All(d => d.HasHardwareNotch))
            return null;

        var section = Section("Position", "Unlocked, drag the closed notch along the top of a display without a camera notch to put it where you want. Each display remembers its own spot. While it's unlocked the notch opens on a click instead of a hover, so you can grab it. On a display with a camera notch it always stays around the camera.");
        section.Cells.Add(Switch("Lock position", "lock.fill", Ui.Orange, settings.NotchPositionLocked, locked => settings.NotchPositionLocked = locked));
        section.Cells.Add(new CommandCell
        {
            Title = "Move Back to the Middle",
            IconSource = Ui.SymbolSource("arrow.left.and.right", 16, Ui.Blue),
            Command = new Command(() => settings.NotchPositions = [])
        });
        return section;
    }

    TableSection NotchSection(AppSettings settings)
    {
        var section = Section("Notch", "The size of the closed notch, either side of the camera. Each side still grows to fit what it's showing: what's playing and any system readings on the left; the weather, a countdown or a meeting on the right. At 0 and 0 with nothing to show, it's just the camera. Hovering opens the notch once the pointer has rested on it for as long as you set, so passing over it on the way to the menu bar doesn't.");

        var sliders = new List<Slider>();
        // All three sliders are always there, the ones the lock doesn't want hidden (see Optional).
        var both = SliderCell("Width each side", "arrow.left.and.right.square", Ui.Teal, settings.NotchLeftWidth, AppSettings.MaxNotchWingWidth, sliders, v =>
        {
            settings.NotchLeftWidth = v;
            settings.NotchRightWidth = v;
        });
        var left = SliderCell("Left of camera", "arrow.left.square", Ui.Teal, settings.NotchLeftWidth, AppSettings.MaxNotchWingWidth, sliders, v => settings.NotchLeftWidth = v);
        var right = SliderCell("Right of camera", "arrow.right.square", Ui.Teal, settings.NotchRightWidth, AppSettings.MaxNotchWingWidth, sliders, v => settings.NotchRightWidth = v);
        var (bothSlider, leftSlider, rightSlider) = (sliders[0], sliders[1], sliders[2]);
        var showBoth = Optional(both, settings.NotchWidthsLocked);
        var showLeft = Optional(left, !settings.NotchWidthsLocked);
        var showRight = Optional(right, !settings.NotchWidthsLocked);

        section.Cells.Add(Switch("Same width both sides", "arrow.left.and.right", Ui.Blue, settings.NotchWidthsLocked, locked =>
        {
            // Locking evens the sides at the left's width; unlocking starts both from where they are.
            if (locked)
                settings.NotchRightWidth = settings.NotchLeftWidth;
            settings.NotchWidthsLocked = locked;
            bothSlider.Value = settings.NotchLeftWidth;
            leftSlider.Value = settings.NotchLeftWidth;
            rightSlider.Value = settings.NotchRightWidth;
            showBoth(locked);
            showLeft(!locked);
            showRight(!locked);
        }));
        section.Cells.Add(both);
        section.Cells.Add(left);
        section.Cells.Add(right);

        var heights = new List<Slider>();
        section.Cells.Add(SliderCell("Extra height", "arrow.up.and.down.square", Ui.Indigo, settings.NotchExtraHeight, AppSettings.MaxNotchExtraHeight, heights, v => settings.NotchExtraHeight = v));

        section.Cells.Add(Button("Reset size", () =>
        {
            foreach (var slider in sliders)
                slider.Value = AppSettings.DefaultNotchWingWidth;
            foreach (var slider in heights)
                slider.Value = 0;
        }));

        // Clicking opens it at once; only hovering waits.
        var hover = StepperCell("Hover this long to open", "timer", Ui.Orange, settings.HoverOpenDelay, 0, AppSettings.MaxHoverOpenDelay, 50, "ms", v => settings.HoverOpenDelay = v);
        var showHover = Optional(hover, !settings.ClickToOpen);
        section.Cells.Add(Switch("Click the notch to open it", "cursorarrow.click", Ui.Blue, settings.ClickToOpen, v =>
        {
            settings.ClickToOpen = v;
            showHover(!v);
        }));
        section.Cells.Add(hover);
        // Buttons on the closed notch only work when hovering doesn't open it first.
        if (settings.ClickToOpen)
        {
            foreach (var info in PeekMusicButtonInfo.All)
            {
                var name = info.Button.ToString();
                section.Cells.Add(Switch($"{info.Name} button on the closed notch", info.Symbol, Ui.Red, settings.PeekMusicButtons.Contains(name), on =>
                {
                    var buttons = settings.PeekMusicButtons.Where(b => b != name).ToList();
                    if (on)
                        buttons.Add(name);
                    settings.PeekMusicButtons = buttons;
                }));
            }
        }
        section.Cells.Add(Switch("Show the new song when it changes", "music.note", Ui.Red, settings.ShowTrackChanges, v => settings.ShowTrackChanges = v));
        section.Cells.Add(Switch("Show volume changes", "speaker.wave.2.fill", Ui.Pink, settings.ShowVolumeHud, v => settings.ShowVolumeHud = v));
        section.Cells.Add(Switch("Show brightness changes", "sun.max.fill", Ui.Yellow, settings.ShowBrightnessHud, v => settings.ShowBrightnessHud = v));
        return section;
    }

    /// <summary>
    /// What the closed notch, or the open panel, is painted with: black (the default), one colour, or a
    /// gradient from top to bottom, or frosted glass. Picking another kind shows its colour rows, in place; a
    /// colour changes in place too, and the notch repaints as it's picked.
    /// </summary>
    TableSection FillSection(AppSettings settings, bool closed)
    {
        NotchFill Current() => closed ? settings.ClosedNotchFill : settings.OpenNotchFill;
        void Save(NotchFill fill)
        {
            if (closed)
                settings.ClosedNotchFill = fill;
            else
                settings.OpenNotchFill = fill;
        }

        // Frosted glass is only offered where the head really blurs behind the window: elsewhere it would be
        // a see-through tint, which reads badly over a busy desktop.
        var canFrost = Ui.Platform.CanFrost;
        var glass = canFrost ? " Frosted glass blurs what's behind the notch under a light tint." : "";
        var section = Section(
            closed ? "Closed Notch Colour" : "Open Notch Colour",
            (closed
                ? "The closed notch, and the volume and brightness it shows when they change. On a MacBook the camera's own cut-out always stays black, so a colour shows either side of it - a gradient from black at the top blends into it."
                : "The panel the notch opens into. A gradient runs from the top to the bottom. The notch's text is white, so deeper colours read best.") + glass
        );
        var fill = Current();
        // Glass saved where it isn't offered (settings brought over from a Mac) becomes its tint, as a colour.
        if (fill.IsGlass && !canFrost)
            Save(fill = fill.From == Colors.Black ? NotchFill.Black : new NotchFill(NotchFillKind.Color, fill.From, fill.From));
        var (fromCell, showFromColor) = ColorCell("Colour", fill.From, c => Save(Current() with { From = c }));
        var (toCell, showToColor) = ColorCell("Bottom", fill.To, c => Save(Current() with { To = c }));
        var showFrom = Optional(fromCell, fill.Kind != NotchFillKind.Black);
        var showTo = Optional(toCell, fill.Kind == NotchFillKind.Gradient);
        void ShowRows(NotchFill shown)
        {
            fromCell.Title = shown.Kind switch { NotchFillKind.Gradient => "Top", NotchFillKind.Glass => "Tint", _ => "Colour" };
            showFromColor(shown.From);
            showToColor(shown.To);
            showFrom(shown.Kind != NotchFillKind.Black);
            showTo(shown.Kind == NotchFillKind.Gradient);
        }
        ShowRows(fill);

        section.Cells.Add(ChoiceCell("Style", "paintbrush.fill", Ui.Pink,
            [(NotchFillKind.Black, "Black"), (NotchFillKind.Color, "Colour"), (NotchFillKind.Gradient, "Gradient"), .. (canFrost ? [(NotchFillKind.Glass, "Frosted Glass")] : Array.Empty<(NotchFillKind, string)>())],
            fill.Kind,
            kind =>
            {
                var now = Current();
                if (kind == now.Kind)
                    return;
                var switched = Switched(now, kind);
                Save(switched);
                ShowRows(switched);
            }));
        section.Cells.Add(fromCell);
        section.Cells.Add(toCell);
        return section;
    }

    static readonly Color FirstColor = NotchFill.Swatches.First(s => s.Name == "Turquoise").Color;

    /// <summary>
    /// A fill changed to another kind, keeping what colours it can. From black it starts on a colour, and a
    /// new gradient starts from black at the top, so it blends into the camera's cut-out.
    /// </summary>
    static NotchFill Switched(NotchFill from, NotchFillKind kind) => (from.Kind, kind) switch
    {
        (_, NotchFillKind.Black) => NotchFill.Black,
        // Glass starts as a dark tint, the closest to the black notch it replaces; from a colour it keeps it.
        (NotchFillKind.Black, NotchFillKind.Glass) => new NotchFill(kind, Colors.Black, Colors.Black),
        (_, NotchFillKind.Glass) => new NotchFill(kind, from.Kind == NotchFillKind.Gradient ? from.To : from.From, from.To),
        (NotchFillKind.Glass, NotchFillKind.Color) when from.From == Colors.Black => new NotchFill(kind, FirstColor, FirstColor),
        (NotchFillKind.Glass, NotchFillKind.Color) => new NotchFill(kind, from.From, from.From),
        (NotchFillKind.Black, NotchFillKind.Color) => new NotchFill(kind, FirstColor, FirstColor),
        (NotchFillKind.Black, NotchFillKind.Gradient) => new NotchFill(kind, Colors.Black, FirstColor),
        // A gradient that starts from black keeps its other colour.
        (NotchFillKind.Gradient, NotchFillKind.Color) => new NotchFill(kind, from.From == Colors.Black ? from.To : from.From, from.To),
        _ => new NotchFill(kind, Colors.Black, from.From)
    };

    /// <summary>
    /// The swatches, and the colour as hex for any other: typing a valid one picks it. The ring marks the
    /// swatch matching the colour, when one does. Also returns a way to show another colour without picking it.
    /// </summary>
    static (CustomCell Cell, Action<Color> Show) ColorCell(string title, Color value, Action<Color> changed)
    {
        const double Size = 20;
        var swatches = new List<(Color Color, Border View)>();
        var hex = new Entry
        {
            Text = value.ToArgbHex(),
            WidthRequest = 84,
            FontSize = 12,
            TextColor = Ui.Text,
            VerticalOptions = LayoutOptions.Center
        };

        void Mark(Color picked) => swatches.ForEach(s => s.View.Stroke = Same(s.Color, picked) ? Ui.Text : Ui.Separator);
        void Pick(Color picked)
        {
            Mark(picked);
            changed(picked);
        }

        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6, VerticalOptions = LayoutOptions.Center };
        foreach (var (swatch, i) in NotchFill.Swatches.Select((s, i) => (s, i)))
        {
            var view = new Border
            {
                WidthRequest = Size,
                HeightRequest = Size,
                BackgroundColor = swatch.Color,
                StrokeThickness = 2,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                AutomationId = swatch.Name
            }.Pressable(() =>
            {
                hex.Text = swatch.Color.ToArgbHex();
                Pick(swatch.Color);
            });
            swatches.Add((swatch.Color, view));
            grid.Children.Add(view.At(row: i / 6, column: i % 6));
        }
        grid.ColumnDefinitions = [.. Enumerable.Range(0, 6).Select(_ => new ColumnDefinition(Size))];
        grid.RowDefinitions = [new RowDefinition(Size), new RowDefinition(Size)];
        Mark(value);

        hex.Completed += (_, _) =>
        {
            if (NotchFill.TryColor(hex.Text, out var typed))
                Pick(typed);
        };

        var cell = new CustomCell
        {
            Title = title,
            IconSource = Ui.SymbolSource("paintpalette.fill", 16, Ui.Orange),
            Description = "Or type a hex colour and press Return",
            IsSelectable = false,
            CustomContent = new HorizontalStackLayout { Spacing = 12, HorizontalOptions = LayoutOptions.End, Children = { grid, hex } }
        };
        // As for switches (see NewSwitchCell): the row's own tap recognizer would take the swatches' clicks.
        cell.GestureRecognizers.Clear();
        return (cell, shown =>
        {
            hex.Text = shown.ToArgbHex();
            Mark(shown);
        });
    }

    /// <summary>
    /// One of a few choices, as a row of pills. Not RadioCells: a picked RadioCell also sets its value on the
    /// whole table, which unticks the radios of every other group - two radio sections on one page undo
    /// each other (and with the same values, picking one picked the other too).
    /// </summary>
    static CustomCell ChoiceCell<T>(string title, string symbol, Color tint, IReadOnlyList<(T Value, string Text)> choices, T value, Action<T> picked)
    {
        var pills = new List<(T Value, Border View, Label Text)>();
        void Mark(T chosen) => pills.ForEach(p =>
        {
            var on = EqualityComparer<T>.Default.Equals(p.Value, chosen);
            p.View.BackgroundColor = on ? Ui.Blue : Ui.Separator;
            p.Text.TextColor = on ? Ui.Text : Ui.Secondary;
        });

        var row = new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        foreach (var (choice, text) in choices)
        {
            var label = new Label { Text = text, FontSize = 12, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
            var pill = new Border
            {
                Padding = new Thickness(10, 4),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Content = label,
                AutomationId = text
            };
            pill.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    Mark(choice);
                    picked(choice);
                })
            });
            pills.Add((choice, pill, label));
            row.Children.Add(pill);
        }
        Mark(value);

        var cell = new CustomCell
        {
            Title = title,
            IconSource = Ui.SymbolSource(symbol, 16, tint),
            IsSelectable = false,
            CustomContent = row
        };
        // As for switches (see NewSwitchCell): the row's own tap recognizer would take the pills' clicks.
        cell.GestureRecognizers.Clear();
        return cell;
    }

    static bool Same(Color a, Color b) => a.ToArgbHex() == b.ToArgbHex();

    /// <summary>
    /// The closed notch on a display without a camera notch. Its sliders show the change on the notch as
    /// they move, but only there: around a real camera they change nothing.
    /// </summary>
    static TableSection StandInSection(AppSettings settings)
    {
        var section = Section("Without a Camera Notch", "On a display with no camera notch, such as an external monitor, there's no camera to leave room for, so the closed notch is only as wide as what it shows. Height is how tall it is before any extra height set above. It never gets narrower than the narrowest width, even with nothing to show, so there's always something to hover.");
        section.Cells.Add(SliderCell("Height", "arrow.up.and.down.square", Ui.Purple, settings.StandInCameraHeight, AppSettings.MaxStandInCameraHeight, [], v => settings.StandInCameraHeight = v, AppSettings.MinStandInCameraHeight));
        section.Cells.Add(SliderCell("Narrowest width", "arrow.left.and.right.square", Ui.Indigo, settings.StandInMinWidth, AppSettings.MaxStandInNotchWidth, [], v => settings.StandInMinWidth = v, AppSettings.MinStandInNotchWidth));
        return section;
    }

    /// <summary>
    /// A switch for everything the closed notch can show. Nothing needs the page rebuilt: the notch picks
    /// each change up as it's made.
    /// </summary>
    static TableSection PeekSection(AppSettings settings)
    {
        var section = Section("Closed Notch", "What shows beside the camera when the notch is closed. A countdown, or a meeting about to start, shows whatever you pick here. System readings update every second while they show.");
        foreach (var info in PeekItemInfo.All)
        {
            var name = info.Item.ToString();
            var cell = Switch(info.Name, info.Symbol, info.Color, settings.PeekItems.Contains(name), on =>
            {
                var items = settings.PeekItems.Where(i => i != name).ToList();
                if (on)
                    items.Add(name);
                settings.PeekItems = items;
            });
            if (info.Item == PeekItem.Sports)
                cell.Description = "Pick it with Show on notch on the game's page in the Sports tab. It shows while the game is on, every 20 seconds, and comes off when it's over.";
            section.Cells.Add(cell);
        }
        return section;
    }

    /// <summary>
    /// A switch for every tab this platform has. The strip spreads whatever is left across the room beside
    /// the camera. The last tab on can't be turned off: an empty strip would leave nothing to open.
    /// </summary>
    static TableSection TabsSection(AppSettings settings, IReadOnlyList<NotchTabInfo> tabs)
    {
        var section = Section("Tabs", "Turn off the tabs you don't use. The rest spread out to fill the strip.");
        foreach (var t in tabs)
        {
            var name = t.Tab.ToString();
            var cell = NewSwitchCell(t.Name, Ui.SymbolSource(t.Symbol, 16, Ui.Blue), !settings.HiddenTabs.Contains(name));
            cell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != SwitchCell.OnProperty.PropertyName)
                    return;

                var hidden = settings.HiddenTabs.Where(h => h != name).ToList();
                if (!cell.On)
                {
                    if (tabs.All(other => other.Tab == t.Tab || hidden.Contains(other.Tab.ToString())))
                    {
                        // After the click: set now, AppKit finishes the click after and leaves it drawn off.
                        cell.Dispatcher.Dispatch(() => cell.On = true);
                        return;
                    }
                    hidden.Add(name);
                }
                settings.HiddenTabs = hidden;
            };
            section.Cells.Add(cell);
        }
        return section;
    }

    TableSection TeleprompterSection(AppSettings settings, TeleprompterService prompter)
    {
        var section = Section("Teleprompter", "Your script scrolls right under the camera, so you read while looking at the lens.");

        var script = new Editor
        {
            Text = settings.TeleprompterScript,
            HeightRequest = 200,
            FontSize = 14,
            TextColor = Ui.Text,
            BackgroundColor = Ui.Surface,
            AutoSize = EditorAutoSizeOption.Disabled
        };
        // Saved once typing pauses, not per keystroke: each save rebuilds an open teleprompter. Leaving the
        // editor saves straight away, so closing the window can't lose the last few keys.
        var scriptEdits = 0;
        var saved = settings.TeleprompterScript;
        void SaveScript()
        {
            if (script.Text is { } text && text != saved)
                settings.TeleprompterScript = saved = text;
        }
        this.flushScript = SaveScript;
        script.TextChanged += (_, _) =>
        {
            var edit = ++scriptEdits;
            this.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
            {
                if (edit == scriptEdits)
                    SaveScript();
            });
        };
        script.Unfocused += (_, _) =>
        {
            scriptEdits++;
            SaveScript();
        };

        section.Cells.Add(new CustomCell { UseFullSize = true, IsSelectable = false, CustomContent = new ContentView { Padding = new Thickness(12, 10), Content = script } });
        section.Cells.Add(StepperCell("Text size", "textformat.size", Ui.Orange, settings.TeleprompterFontSize, 12, 48, 2, "pt", v => settings.TeleprompterFontSize = v));
        section.Cells.Add(StepperCell("Speed", "speedometer", Ui.Green, settings.TeleprompterSpeed, 5, 200, 4, "pt/s", v => settings.TeleprompterSpeed = v));
        section.Cells.Add(StepperCell("Width", "arrow.left.and.right.square", Ui.Teal, settings.TeleprompterWidth, AppSettings.MinTeleprompterWidth, AppSettings.MaxTeleprompterWidth, 40, "pt", v => settings.TeleprompterWidth = v));
        section.Cells.Add(StepperCell("Height", "arrow.up.and.down.square", Ui.Indigo, settings.TeleprompterHeight, AppSettings.MinTeleprompterHeight, AppSettings.MaxTeleprompterHeight, 20, "pt", v => settings.TeleprompterHeight = v));
        // Only where there's a recognizer to follow with (Linux has no OS speech engine).
        if (prompter.CanListen)
            section.Cells.Add(Switch("Follow my voice with speech-to-text", "mic.fill", Ui.Red, settings.TeleprompterFollowVoice, v => settings.TeleprompterFollowVoice = v));
        return section;
    }

    static TableSection CalculatorSection(AppSettings settings, CalculatorService calculator)
    {
        var section = Section("Calculator", "Clicked, not typed: the notch never takes the keyboard. Copying (the button beside the number) leaves out the thousands separators, so it pastes as a number anywhere.");
        section.Cells.Add(Switch("Group thousands (1,234,567)", "textformat.123", Ui.Orange, settings.CalculatorGroupDigits, v => settings.CalculatorGroupDigits = v));
        section.Cells.Add(Switch("Show the history beside the keypad", "clock.arrow.circlepath", Ui.Teal, settings.CalculatorShowTape, v => settings.CalculatorShowTape = v));
        section.Cells.Add(Switch("Copy each answer when I press =", "doc.on.doc", Ui.Blue, settings.CalculatorCopyOnEquals, v => settings.CalculatorCopyOnEquals = v));
        section.Cells.Add(Button("Clear the history", calculator.ClearTape));
        return section;
    }

    static TableSection KeepAwakeSection(AppSettings settings)
    {
        var section = Section("Keep Awake", "Stops the computer going to sleep, or the screen going dark, while you're away from the keyboard. Start it from the Keep Awake tab, or click the ring there, for a while or until you stop it. Closing the lid or choosing Sleep still sleeps it.");
        section.Cells.Add(Switch("Keep the screen on too", "display", Ui.Yellow, settings.AwakeKeepDisplayOn, v => settings.AwakeKeepDisplayOn = v));
        section.Cells.Add(Switch("Start keeping awake when Notcho opens", "power", Ui.Green, settings.AwakeOnLaunch, v => settings.AwakeOnLaunch = v));
        section.Cells.Add(Switch("Show a cup on the closed notch while it's on", "cup.and.saucer.fill", Ui.Yellow, settings.AwakeShowInPeek, v => settings.AwakeShowInPeek = v));
        return section;
    }

    TableSection WeatherSection(AppSettings settings, WeatherService weather)
    {
        // On Windows and Linux your location's name comes from OpenStreetMap, whose terms ask for this wherever
        // it's shown. The Mac names it with Apple's geocoder.
        var section = Section("Weather", OperatingSystem.IsMacOS() ? null : $"Place names for your location: {Shiny.Locations.NominatimOptions.Attribution}");

        var allow = Button("Allow location access", weather.RequestLocationAccess);
        var showAllow = Optional(allow, false);
        SwitchCell? here = null;
        // The location access note and button that go with the switch.
        void ShowAccess(bool on)
        {
            here!.Description = on && weather.IsLocationDenied ? "Location access is off: allow it in your system's privacy settings." : "";
            showAllow(on && !weather.IsLocationDenied && !weather.IsLocationAuthorized);
        }
        here = Switch("Show the weather where I am", "location.fill", Ui.Blue, settings.WeatherUseCurrentLocation, v =>
        {
            settings.WeatherUseCurrentLocation = v;
            if (v)
                weather.RequestLocationAccess();
            _ = weather.RefreshAsync(force: true);
            ShowAccess(v);
        });
        ShowAccess(settings.WeatherUseCurrentLocation);
        section.Cells.Add(here);
        section.Cells.Add(allow);
        return section;
    }

    TableSection TemperatureSection(AppSettings settings, WeatherService weather)
    {
        var section = Section("Temperature", "Automatic follows your region. You can also tap the temperature in the notch to switch.");
        foreach (var (key, text) in new[] { ("auto", "Automatic"), ("c", "Celsius (°C)"), ("f", "Fahrenheit (°F)") })
            section.Cells.Add(new RadioCell { Title = text, Value = key });

        RadioCell.SetSelectedValue(section, settings.TemperatureUnit);
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == RadioCell.SelectedValueProperty.PropertyName && RadioCell.GetSelectedValue(section) is string key && key != settings.TemperatureUnit)
                weather.SetUnit(key);
        };
        return section;
    }

    TableSection PlacesSection(AppSettings settings, WeatherService weather)
    {
        var places = settings.WeatherPlaces;
        var section = Section("Places", places.Count == 0 ? "No other places yet." : "Use ‹ › in the notch to switch between them.");

        foreach (var place in places)
        {
            var remove = new Button { Text = "Remove", FontSize = 12, Padding = new Thickness(10, 2), VerticalOptions = LayoutOptions.Center };
            remove.Clicked += (_, _) =>
            {
                weather.Remove(place);
                this.Rebuild();
            };
            section.Cells.Add(new CustomCell { Title = place.Name, IconSource = Ui.SymbolSource("mappin.circle.fill", 16, Ui.Red), IsSelectable = false, CustomContent = remove });
        }

        var search = new EntryCell { Title = "Add a city", Placeholder = "e.g. Toronto", ValueText = searchText, IconSource = Ui.SymbolSource("magnifyingglass", 16, Ui.Secondary) };
        async void Search()
        {
            searchText = search.ValueText ?? "";
            if (searchText.Trim().Length == 0)
                return;
            searchResults = await weather.SearchAsync(searchText);
            if (searchResults.Count == 0)
                searchResults = [new WeatherPlace("", 0, 0)]; // a "nothing found" marker
            this.Rebuild();
        }
        search.Completed += (_, _) => Search();
        section.Cells.Add(search);

        foreach (var result in searchResults)
        {
            if (result.Name.Length == 0)
            {
                section.Cells.Add(new LabelCell { Title = $"Nothing found for \"{searchText}\"", IsSelectable = false, TitleColor = Ui.Secondary });
                continue;
            }

            section.Cells.Add(new CommandCell
            {
                Title = result.Name,
                IconSource = Ui.SymbolSource("plus.circle.fill", 16, Ui.Green),
                ShowArrow = false,
                Command = new Command(() =>
                {
                    weather.Add(result);
                    searchText = "";
                    searchResults = [];
                    this.Rebuild();
                })
            });
        }
        return section;
    }

    static TableSection LeaguesSection(SportsService sports)
    {
        var section = Section("Leagues", "The leagues across the top of the Sports tab. Scores, schedules, stats, standings and logos come from ESPN.");
        var followed = sports.Leagues.Select(l => l.Key).ToHashSet();
        foreach (var league in SportsLeague.All)
        {
            var key = league.Key;
            var cell = Switch(league.Name, "sportscourt.fill", Ui.Green, followed.Contains(key), on => sports.SetLeagueFollowed(key, on));
            SetLogoIcon(sports, league.Logo, cell);
            section.Cells.Add(cell);
        }
        return section;
    }

    /// <summary>
    /// A league's or team's logo on its row: straight away when it's loaded, otherwise set on the row in
    /// place when it arrives, so the page isn't built again once per logo. The row's own symbol stays if
    /// it can't be had.
    /// </summary>
    static async void SetLogoIcon(SportsService sports, string? url, CellBase cell)
    {
        if (url == null)
            return;
        var cached = sports.CachedLogo(url);
        if (cached != null)
        {
            cell.IconSource = SportsLogo.FromBytes(cached);
            return;
        }
        var bytes = await sports.LogoAsync(url);
        if (bytes != null)
            cell.IconSource = SportsLogo.FromBytes(bytes);
    }

    /// <summary>
    /// Your teams, then a box that suggests teams from the followed leagues as you type. A team is only
    /// ever added by picking a suggestion, so what's typed can't become a team ESPN doesn't know.
    /// </summary>
    TableSection TeamsSection(SportsService sports)
    {
        var favourites = sports.Favourites;
        var section = Section("My Teams", favourites.Count == 0
            ? "No teams yet. Start typing a team from a league you follow and pick it from the list, or tap the star on a team's page in the notch."
            : "Your teams' games come first, and the closed notch can show their live scores.");

        // The box first, above the teams: its suggestions open below it and don't flip up when the window
        // runs out, so it has to sit high in the window however many teams there are.
        this.AddTeamPicker(section, sports, favourites);

        foreach (var team in favourites)
        {
            var remove = new Button { Text = "Remove", FontSize = 12, Padding = new Thickness(10, 2), VerticalOptions = LayoutOptions.Center };
            remove.Clicked += (_, _) =>
            {
                sports.ToggleFavourite(team);
                this.Rebuild();
            };
            var cell = new CustomCell { Title = team.Name, Description = SportsLeague.Find(team.League)?.Name ?? "", IconSource = Ui.SymbolSource("star.fill", 16, Ui.Yellow), IsSelectable = false, CustomContent = remove };
            SetLogoIcon(sports, team.Logo, cell);
            section.Cells.Add(cell);
        }
        return section;
    }

    /// <summary>Suggestion rows under the team box: enough to find a team, few enough to stay in view.</summary>
    const int TeamSuggestions = 6;

    /// <summary>
    /// The add-a-team box and its suggestions: teams from the followed leagues matching what's typed,
    /// each with its logo and league, the teams already yours left out. A click on one adds it, and so
    /// does Return for the first; typed text on its own never becomes a team.
    /// </summary>
    /// <remarks>
    /// The suggestions are rows of the table, not a list floating over it: a floating list closed as the
    /// box lost focus to the click meant for it, and ran off the bottom of the window. The rows are all
    /// built now and shown or hidden in place, since the AppKit head never draws rows added to a page
    /// already on screen (see <see cref="Optional"/>).
    /// </remarks>
    void AddTeamPicker(TableSection section, SportsService sports, IReadOnlyList<SportsTeam> favourites)
    {
        // The leagues' team lists, fetched now so the first letters typed have something to search.
        _ = sports.PreloadTeamsAsync();

        var box = new Entry
        {
            AutomationId = "TeamPicker",
            BackgroundColor = Colors.Transparent,
            Placeholder = "Add a team, e.g. Maple Leafs",
            PlaceholderColor = Ui.Secondary,
            TextColor = Ui.Text,
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center
        };
        section.Cells.Add(new CustomCell
        {
            UseFullSize = true,
            IsSelectable = false,
            CustomContent = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
                ColumnSpacing = 10,
                Padding = new Thickness(14, 8),
                Children =
                {
                    new Image { Source = Ui.SymbolSource("magnifyingglass", 16, Ui.Secondary), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                    box.At(column: 1)
                }
            }
        });

        // Adds, never toggles, and only once per page: on the AppKit head the box sends Completed when it
        // loses focus to the click on a suggestion, so a click arrives as two picks, and a toggle undid
        // the first with the second.
        var picked = false;
        void Pick(SportsTeam team)
        {
            if (picked)
                return;
            picked = true;
            if (!sports.IsFavourite(team))
                sports.ToggleFavourite(team);
            this.Rebuild();
        }

        var shown = new SportsTeam?[TeamSuggestions];
        var rows = new CommandCell[TeamSuggestions];
        var show = new Action<bool>[TeamSuggestions];
        for (var i = 0; i < TeamSuggestions; i++)
        {
            var index = i;
            rows[i] = new CommandCell
            {
                ShowArrow = false,
                IconSource = Ui.SymbolSource("plus.circle.fill", 16, Ui.Green),
                Command = new Command(() =>
                {
                    if (shown[index] is { } team)
                        Pick(team);
                })
            };
            show[i] = Optional(rows[i], false);
            section.Cells.Add(rows[i]);
        }

        var none = new LabelCell { IsSelectable = false, TitleColor = Ui.Secondary };
        var showNone = Optional(none, false);
        section.Cells.Add(none);

        var searches = 0;
        async void Search(string text)
        {
            var search = ++searches;
            var found = text.Trim().Length == 0 ? [] : await sports.SearchTeamsAsync(text);
            // Typing moved on while this one was out.
            if (search != searches)
                return;

            var teams = found.Where(t => favourites.All(f => f.Key != t.Key)).Take(TeamSuggestions).ToList();
            for (var i = 0; i < TeamSuggestions; i++)
            {
                var index = i;
                var team = index < teams.Count ? teams[index] : null;
                shown[index] = team;
                show[index](team != null);
                if (team != null)
                {
                    rows[index].Title = team.Name;
                    rows[index].Description = SportsLeague.Find(team.League)?.Name ?? "";
                    SetSuggestionLogo(sports, rows[index], team, () => shown[index]);
                }
            }
            none.Title = $"No team called \"{text.Trim()}\" in the leagues you follow";
            showNone(text.Trim().Length > 0 && teams.Count == 0);
        }

        var edits = 0;
        box.TextChanged += (_, e) =>
        {
            var edit = ++edits;
            var text = e.NewTextValue ?? "";
            this.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
            {
                if (edit == edits)
                    Search(text);
            });
        };
        // Return adds the first suggestion - after a moment, because the same Completed also arrives when a
        // click on a suggestion takes the focus, and the team clicked must win over the first.
        box.Completed += (_, _) => this.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(350), () =>
        {
            if (shown[0] is { } first)
                Pick(first);
        });
    }

    /// <summary>
    /// A suggestion's logo, set on its row when it arrives - unless typing has since put another team in
    /// that row, whose own logo is on its way.
    /// </summary>
    static async void SetSuggestionLogo(SportsService sports, CellBase row, SportsTeam team, Func<SportsTeam?> current)
    {
        var placeholder = Ui.SymbolSource("plus.circle.fill", 16, Ui.Green);
        if (team.Logo == null)
        {
            row.IconSource = placeholder;
            return;
        }
        if (sports.CachedLogo(team.Logo) is { } cached)
        {
            row.IconSource = SportsLogo.FromBytes(cached);
            return;
        }
        row.IconSource = placeholder;
        var bytes = await sports.LogoAsync(team.Logo);
        if (bytes != null && current()?.Key == team.Key)
            row.IconSource = SportsLogo.FromBytes(bytes);
    }

    static TableSection NewsNetworksSection(AppSettings settings, NewsService news)
    {
        var section = Section("News networks", "Where the News tab's headlines come from. A click opens the story on the network's own site.");
        var chosen = settings.NewsNetworks;
        foreach (var network in NewsNetwork.All)
        {
            var key = network.Key;
            var color = Color.FromArgb(network.Color);
            var tint = color.GetLuminosity() < 0.5f ? color.WithLuminosity(0.6f) : color;
            var cell = Switch(network.Name, "newspaper.fill", tint, chosen.Contains(key), on => news.SetNetwork(key, on));
            var covers = String.Join(", ", NewsCategory.All.Where(c => network.Feeds.ContainsKey(c.Key)).Select(c => c.Name));
            cell.Description = network.Note == null ? covers : $"{covers}. {network.Note}";
            section.Cells.Add(cell);
        }
        return section;
    }

    static TableSection NewsCategoriesSection(AppSettings settings, NewsService news)
    {
        var section = Section("News categories", "A chip on the News tab for each, with that category's stories from every network above that covers it.");
        var chosen = settings.NewsCategories;
        foreach (var newsCategory in NewsCategory.All)
        {
            var key = newsCategory.Key;
            section.Cells.Add(Switch(newsCategory.Name, "list.bullet", Ui.Red, chosen.Contains(key), on => news.SetCategory(key, on)));
        }
        return section;
    }

    /// <summary>The RSS tab's feeds, each removable, and a box to add another by its address or its site's.</summary>
    TableSection RssSection(RssService rss)
    {
        var feeds = rss.Feeds;
        var section = Section("RSS feeds", feeds.Count == 0
            ? "No feeds yet. Paste a feed's address, or just a site's, and press Return: Notcho finds the feed a site advertises."
            : "Each feed is a chip on the RSS tab, after All.");

        foreach (var feed in feeds)
        {
            var remove = new Button { Text = "Remove", FontSize = 12, Padding = new Thickness(10, 2), VerticalOptions = LayoutOptions.Center };
            remove.Clicked += (_, _) =>
            {
                rss.Remove(feed.Url);
                this.Rebuild();
            };
            var host = Uri.TryCreate(feed.Url, UriKind.Absolute, out var uri) ? uri.Host : feed.Url;
            section.Cells.Add(new CustomCell { Title = feed.Title, Description = host, IconSource = Ui.SymbolSource("dot.radiowaves.up.forward", 16, Ui.Orange), IsSelectable = false, CustomContent = remove });
        }

        var add = new EntryCell { Title = "Add a feed", Placeholder = "e.g. daringfireball.net", ValueText = rssText, IconSource = Ui.SymbolSource("link", 16, Ui.Secondary) };
        async void Add()
        {
            rssText = add.ValueText ?? "";
            if (rssText.Trim().Length == 0)
                return;
            add.Description = "Looking for a feed…";
            rssError = await rss.AddAsync(rssText);
            if (rssError == null)
                rssText = "";
            this.Rebuild();
        }
        add.Completed += (_, _) => Add();
        section.Cells.Add(add);

        if (rssError != null)
            section.Cells.Add(new LabelCell { Title = rssError, IsSelectable = false, TitleColor = Ui.Orange });
        return section;
    }

    /// <summary>
    /// Back up your settings to a file wherever you choose, and restore from one. A restore checks the
    /// whole file first (see <see cref="AppSettings.Verify"/>), and changes nothing unless it all passes.
    /// </summary>
    TableSection BackupSection(AppSettings settings, IFileDialogs dialogs, TimeProvider time)
    {
        var section = Section("Backup & Restore", "Save your settings to a file, or put them back from one: on another computer too. A file is checked before anything changes, and one that's damaged or edited is turned away. Your tasks, clipboard history and tray files aren't included.");

        section.Cells.Add(Button("Back Up to File…", async () =>
        {
            var now = time.GetLocalNow();
            var path = await dialogs.SaveAsync("Back Up Notcho Settings", $"Notcho Settings {now:yyyy-MM-dd}.json");
            if (path == null)
                return;
            try
            {
                await File.WriteAllTextAsync(path, settings.Export(now));
                backupStatus = $"Backed up to {Path.GetFileName(path)}.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                backupStatus = $"Couldn't save the backup: {ex.Message}";
            }
            this.Rebuild();
        }));

        section.Cells.Add(Button("Restore from File…", async () =>
        {
            var path = await dialogs.OpenAsync("Restore Notcho Settings");
            if (path == null)
                return;
            backupStatus = await RestoreAsync(settings, path);
            this.Rebuild();
        }));

        if (backupStatus != null)
            section.Cells.Add(new LabelCell { Title = backupStatus, IsSelectable = false, TitleColor = Ui.Secondary });
        return section;
    }

    /// <summary>Verifies the file, and only then restores it. What happened, to show under the buttons.</summary>
    static async Task<string> RestoreAsync(AppSettings settings, string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            if (new FileInfo(path).Length > AppSettings.MaxBackupBytes)
                return $"Didn't restore {name}: it's far too big to be a Notcho settings backup.";
            var backup = settings.Verify(await File.ReadAllTextAsync(path));
            var count = settings.Restore(backup);
            var made = backup.Created is { } created ? $" (backed up {created.LocalDateTime:g})" : "";
            var skipped = backup.Unknown > 0 ? $" {backup.Unknown} from a newer version of Notcho were left out." : "";
            return $"Restored {count} settings from {name}{made}.{skipped}";
        }
        catch (FormatException ex)
        {
            return $"Didn't restore {name}. {ex.Message} Nothing was changed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't read {name}: {ex.Message}";
        }
    }

    TableSection PermissionsSection(CalendarService calendar)
    {
        var section = Section("Permissions", "Now Playing asks once for permission to talk to Music and Spotify, the first time either is open.");

        section.Cells.Add(new LabelCell
        {
            Title = "Calendar",
            IconSource = Ui.SymbolSource("calendar", 16, Ui.Red),
            IsSelectable = false,
            ValueText = calendar.IsAuthorized ? "Allowed" : calendar.IsDenied ? "Turned off" : "Not asked yet",
            Description = calendar.IsDenied ? "Allow it in your system's privacy settings." : ""
        });

        if (!calendar.IsAuthorized && !calendar.IsDenied)
        {
            section.Cells.Add(Button("Allow calendar access", async () =>
            {
                await calendar.RequestAccessAsync();
                this.Rebuild();
            }));
        }
        return section;
    }

    /// <summary>
    /// A newer release, whenever there is one - "Don't show again" only stops the window asking. The
    /// install runs here too, its progress in the version row; a failure stays there to read.
    /// </summary>
    static TableSection? UpdateSection(UpdateService updates)
    {
        if (updates.Available is not { } release)
            return null;

        var section = Section("Update", null);
        var version = new LabelCell
        {
            Title = $"Notcho {release.DisplayVersion} is available",
            Description = $"You have {updates.CurrentVersion}.",
            IconSource = Ui.SymbolSource("arrow.down.circle.fill", 16, Ui.Blue),
            IsSelectable = false
        };
        var install = Button("Install and Restart", () => { });
        install.Command = new Command(async () =>
        {
            install.IsEnabled = false;
            version.Description = "Downloading…";
            try
            {
                await updates.InstallAsync(new Progress<double>(p =>
                    version.Description = p >= 1 ? "Installing…" : $"Downloading… {p:P0}"));
            }
            catch (Exception ex)
            {
                version.Description = $"Couldn't install the update: {ex.Message}";
                install.IsEnabled = true;
            }
        });
        install.IsEnabled = !updates.IsInstalling;

        section.Cells.Add(version);
        section.Cells.Add(new CommandCell
        {
            Title = "Release Notes",
            IconSource = Ui.SymbolSource("doc.text", 16, Ui.Secondary),
            Command = new Command(() => updates.ShowRelease?.Invoke(release))
        });
        section.Cells.Add(install);
        return section;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        this.updates.Changed += this.OnUpdatesChanged;
        this.notches.DisplaysChanged += this.OnDisplaysChanged;
        // Found while the page was away.
        this.OnUpdatesChanged(this, EventArgs.Empty);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.updates.Changed -= this.OnUpdatesChanged;
        this.notches.DisplaysChanged -= this.OnDisplaysChanged;
    }

    /// <summary>A display plugged in or out: the Displays section lists what's connected now.</summary>
    void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (category == SettingsCategory.Notch)
            this.Dispatcher.Dispatch(() => this.Rebuild());
    }

    void OnUpdatesChanged(object? sender, EventArgs e)
    {
        if (this.updates.Available?.Version != this.shownUpdate)
            this.Dispatcher.Dispatch(() => this.Rebuild());
    }

    /// <summary>
    /// The launch-at-login switch. It shows what macOS actually decided, not what was asked: the first
    /// registration usually needs approving in System Settings, and the user can turn the login item off
    /// there at any time. So the state is read fresh each time the page is built, never cached.
    /// </summary>
    static TableSection GeneralSection(IAppStartupInstaller startup, IPlatformShell shell)
    {
        var section = Section("General", null);
        if (!startup.IsSupported)
        {
            section.Cells.Add(new LabelCell { Title = "Open at login", Description = "Not available on this system.", IsSelectable = false });
            section.Cells.Add(QuitButton(shell));
            return section;
        }

        var syncing = false;
        var login = NewSwitchCell("Open Notcho when you log in", Ui.SymbolSource("power", 16, Ui.Green), false);
        var openSettings = Button("Open Login Items", async () => await startup.OpenSettings());

        void Show(AppStartupState state)
        {
            syncing = true;
            login.On = state is AppStartupState.Enabled or AppStartupState.RequiresApproval;
            syncing = false;

            login.Description = state switch
            {
                AppStartupState.RequiresApproval => $"Almost: allow Notcho in {shell.LoginItemsLocation}.",
                AppStartupState.DisabledByUser => $"Turned off in {shell.LoginItemsLocation}.",
                AppStartupState.DisabledByPolicy => "Your organisation's settings don't allow this.",
                _ => ""
            };
            openSettings.IsEnabled = state is AppStartupState.RequiresApproval or AppStartupState.DisabledByUser;
        }

        login.PropertyChanged += async (_, e) =>
        {
            if (syncing || e.PropertyName != SwitchCell.OnProperty.PropertyName)
                return;

            try
            {
                Show(login.On ? await startup.Register() : await startup.Unregister());
            }
            catch (Exception ex)
            {
                login.Description = $"Couldn't change the login item: {ex.Message}";
            }
        };

        // Always present (the AppKit head won't add it later); enabled only when it has something to do.
        openSettings.IsEnabled = false;
        _ = Load();
        async Task Load() => Show(await startup.GetState());

        section.Cells.Add(login);
        section.Cells.Add(openSettings);
        section.Cells.Add(QuitButton(shell));
        return section;
    }

    /// <summary>
    /// Check for Updates says how it went in the version row. A newer release rebuilds the page (see
    /// <see cref="OnUpdatesChanged"/>) with the Update section at the top.
    /// </summary>
    static TableSection AboutSection(IPlatformShell shell, UpdateService updates)
    {
        var section = Section("About", "Notcho is built on the open-source Shiny .NET libraries.");
        var version = new LabelCell { Title = "Version", ValueText = updates.CurrentVersion, Description = "", IconSource = Ui.SymbolSource("info.circle.fill", 16, Ui.Secondary), IsSelectable = false };
        section.Cells.Add(version);
        if (updates.IsSupported)
        {
            var check = Button("Check for Updates", () => { });
            check.Command = new Command(async () =>
            {
                check.IsEnabled = false;
                version.Description = "Checking…";
                var reached = await updates.CheckAsync();
                version.Description = !reached
                    ? "Couldn't reach GitHub. Try again in a bit."
                    : updates.Available is { } release
                        ? $"Notcho {release.DisplayVersion} is available - see the top of this page."
                        : "You're up to date.";
                check.IsEnabled = true;
            });
            section.Cells.Add(check);
        }
        section.Cells.Add(new CommandCell
        {
            Title = "Notcho Website",
            ValueText = "shinysoftware.github.io/Notcho",
            IconSource = Ui.SymbolSource("globe", 16, Ui.Orange),
            Command = new Command(() => _ = Launcher.OpenAsync("https://shinysoftware.github.io/Notcho/"))
        });
        section.Cells.Add(new CommandCell
        {
            Title = "ShinySoft Technologies",
            ValueText = "shinysoft.biz",
            IconSource = Ui.SymbolSource("sparkles", 16, Ui.Yellow),
            Command = new Command(() => _ = Launcher.OpenAsync("https://shinysoft.biz"))
        });
        section.Cells.Add(new CommandCell
        {
            Title = "Shiny .NET",
            ValueText = "shinylib.net",
            IconSource = Ui.SymbolSource("shippingbox.fill", 16, Ui.Purple),
            Command = new Command(() => _ = Launcher.OpenAsync("https://www.shinylib.net"))
        });
        return section;
    }

    /// <summary>
    /// Quitting, in General - the first thing Settings shows. With no Dock or menu-bar icon, Settings is
    /// the only way out, so it shouldn't be hidden at the end of About.
    /// </summary>
    static ButtonCell QuitButton(IPlatformShell shell)
    {
        var quit = Button("Quit Notcho", shell.Quit);
        quit.ButtonTextColor = Ui.Red;
        return quit;
    }

    // ---------------------------------------------------------------------------------------------
    // Cells
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The app's icon, for the top of the page: embedded in Core rather than taken from the head, so every
    /// platform has it (the Mac's app icon is an asset catalog, not an image a view can show).
    /// </summary>
    static ImageSource AppIcon() => ImageSource.FromStream(() => typeof(SettingsPage).Assembly.GetManifestResourceStream("Notcho.AppIcon.png")!);

    /// <summary>The icon beside the name and slogan, then a line on getting about, then the first section's heading.</summary>
    /// <summary>
    /// The logo and name, and a chip per category under them. Outside the table, so it stays at the top
    /// while the category's settings scroll beneath it.
    /// </summary>
    /// <param name="updatePending">General gets a dot while a new version waits there.</param>
    View Header(bool updatePending)
    {
        var top = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 12
        };
        top.Children.Add(new Image { Source = AppIcon(), WidthRequest = 56, HeightRequest = 56, VerticalOptions = LayoutOptions.Center }.At(column: 0));
        top.Children.Add(new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "Notcho", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Ui.Text },
                new Label { Text = "Notcho Notcho Man! We got all the fixins.", FontSize = 12, FontAttributes = FontAttributes.Bold | FontAttributes.Italic, TextColor = Ui.Orange }
            }
        }.At(column: 1));

        var chips = new HorizontalStackLayout { Spacing = 6 };
        foreach (var info in CategoryInfo)
        {
            var next = info.Category;
            var name = next == SettingsCategory.General && updatePending ? info.Name + " •" : info.Name;
            // Its AutomationId is its text (see Ui.Chip), which UI tests find it by.
            chips.Children.Add(Ui.Chip(name, () => this.ShowCategory(next), next == category));
        }

        return new VerticalStackLayout
        {
            Spacing = 14,
            Padding = new Thickness(20, 18, 20, 10),
            BackgroundColor = Ui.Surface,
            Children =
            {
                top,
                // A plain row, not a sideways scroller: on the AppKit head that measured short and the
                // table drew over the chips. Seven words fit across the window.
                chips,
                new BoxView { HeightRequest = 1, Color = Ui.Separator, Margin = new Thickness(-20, 2, -20, 0) }
            }
        };
    }

    static readonly (SettingsCategory Category, string Name)[] CategoryInfo =
    [
        (SettingsCategory.General, "General"),
        (SettingsCategory.Notch, "Notch"),
        (SettingsCategory.Tabs, "Tabs"),
        (SettingsCategory.Weather, "Weather"),
        (SettingsCategory.Sports, "Sports"),
        (SettingsCategory.News, "News"),
        (SettingsCategory.About, "About")
    ];

    static TableSection Section(string title, string? footer)
    {
        var section = new TableSection(title.ToUpperInvariant());
        if (footer != null)
            section.FooterText = footer;
        return section;
    }

    /// <summary>
    /// A switch row that only the switch toggles. Every cell carries a tap recognizer, and on the AppKit head
    /// that is a click recognizer that takes the mouse-down from the switch inside it - and with the row
    /// not selectable, does nothing with it, so clicking the switch did nothing at all.
    /// </summary>
    static SwitchCell NewSwitchCell(string title, ImageSource icon, bool on)
    {
        var cell = new SwitchCell { Title = title, On = on, IconSource = icon, IsSelectable = false };
        cell.GestureRecognizers.Clear();
        return cell;
    }

    static SwitchCell Switch(string title, string symbol, Color tint, bool value, Action<bool> changed)
    {
        var cell = NewSwitchCell(title, Ui.SymbolSource(symbol, 16, tint), value);
        cell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == SwitchCell.OnProperty.PropertyName)
                changed(cell.On);
        };
        return cell;
    }

    /// <summary>
    /// A row that comes and goes, shown and hidden in place with the returned action - not by building the
    /// page again, which flashed the whole window and lost its scroll position. The AppKit head never
    /// realises a row first shown on a page that's already on screen, so every such row is drawn, and only
    /// then hidden if it isn't wanted; a row the table has drawn hides and shows itself.
    /// </summary>
    static Action<bool> Optional(CellBase cell, bool visible)
    {
        var wanted = visible;
        var drawn = false;
        cell.HandlerChanged += (_, _) =>
        {
            if (drawn || cell.Handler == null)
                return;
            drawn = true;
            cell.Dispatcher.Dispatch(() => cell.IsVisible = wanted);
        };
        return show =>
        {
            wanted = show;
            if (drawn)
                cell.IsVisible = show;
        };
    }

    static ButtonCell Button(string title, Action tapped)
        => new() { Title = title, ButtonTextColor = Ui.Blue, Command = new Command(tapped) };

    /// <summary>Whole points only, and only on a change: every save re-renders the notch, and a drag sends many.</summary>
    static CustomCell SliderCell(string title, string symbol, Color tint, double value, double max, List<Slider> sliders, Action<double> changed, double min = 0)
    {
        var shown = new Label { Text = $"{value:0} pt", TextColor = Ui.Secondary, FontSize = 12, WidthRequest = 44, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center };
        var slider = Ui.Slider(min, max, value, 1, tint, Ui.Purple);
        slider.WidthRequest = 220;
        var last = Math.Round(value);
        slider.ValueChangedEvent += (_, v) =>
        {
            var rounded = Math.Round(v);
            if (rounded == last)
                return;
            last = rounded;
            shown.Text = $"{rounded:0} pt";
            changed(rounded);
        };
        sliders.Add(slider);

        return new CustomCell
        {
            Title = title,
            IconSource = Ui.SymbolSource(symbol, 16, tint),
            IsSelectable = false,
            CustomContent = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { slider, shown } }
        };
    }

    static CustomCell StepperCell(string title, string symbol, Color tint, double value, double min, double max, double step, string unit, Action<double> changed)
    {
        var shown = new Label { Text = $"{value:0} {unit}", TextColor = Ui.Secondary, FontSize = 12, VerticalOptions = LayoutOptions.Center };
        var stepper = new Stepper { Minimum = min, Maximum = max, Increment = step, Value = value, VerticalOptions = LayoutOptions.Center };
        stepper.ValueChanged += (_, e) =>
        {
            shown.Text = $"{e.NewValue:0} {unit}";
            changed(e.NewValue);
        };

        return new CustomCell
        {
            Title = title,
            IconSource = Ui.SymbolSource(symbol, 16, tint),
            IsSelectable = false,
            CustomContent = new HorizontalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.End, Children = { shown, stepper } }
        };
    }

    /// <summary>Shows another category: a fresh page, from the top.</summary>
    void ShowCategory(SettingsCategory next)
    {
        if (next == category)
            return;
        category = next;
        this.Rebuild(keepScroll: false);
    }

    /// <param name="keepScroll">The fresh page carries on from where this one was scrolled to.</param>
    void Rebuild(bool keepScroll = true)
    {
        // The fresh page reads the script from settings, so an edit still in its debounce goes there first.
        this.flushScript?.Invoke();
        keptScroll = null;
        if (keepScroll && this.table.Content is ScrollView scroller)
            keptScroll = scroller.ScrollY;
        if (this.Window is { } window && IPlatformApplication.Current?.Services.GetService<SettingsPage>() is { } fresh)
            window.Page = fresh;
    }
}

/// <summary>The groups Settings is split into, a chip each under the header.</summary>
enum SettingsCategory
{
    General,
    Notch,
    Tabs,
    Weather,
    Sports,
    News,
    About
}

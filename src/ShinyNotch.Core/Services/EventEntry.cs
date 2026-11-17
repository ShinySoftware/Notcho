using Shiny;
using Shiny.Maui.Controls.QuickEntry;
using ShinyNotch.Views;

namespace ShinyNotch.Services;

/// <summary>
/// Adding a calendar event. The notch window never takes the keyboard, so it's a small form in the shared
/// quick entry popup (see <see cref="EntryWindow"/>): the title, where, the day, from and to or all day, and
/// which calendar. Return in the title or Add saves it; Escape, Cancel or clicking away closes it.
/// </summary>
[Singleton]
public sealed class EventEntry
{
    const double LabelWidth = 76;

    readonly IQuickEntryService quickEntry;
    readonly CalendarService calendar;
    readonly TimeProvider time;

    public EventEntry(IQuickEntryService quickEntry, CalendarService calendar, TimeProvider time)
    {
        this.quickEntry = quickEntry;
        this.calendar = calendar;
        this.time = time;
    }

    public bool IsOpen => this.quickEntry.IsOpen;

    /// <summary>The popup closed, whichever way.</summary>
    public event EventHandler? Closed
    {
        add => this.quickEntry.Closed += value;
        remove => this.quickEntry.Closed -= value;
    }

    /// <summary>Opens the form on <paramref name="day"/>, an hour long from the next hour if that's today, else from 9.</summary>
    public void Show(DateTime day) => EntryWindow.Show(this.quickEntry, 460, () => this.Form(day.Date));

    View Form(DateTime day)
    {
        var now = this.time.GetLocalNow().DateTime;
        var start = day == now.Date && now.Hour < 23 ? TimeSpan.FromHours(now.Hour + 1) : TimeSpan.FromHours(9);
        var length = TimeSpan.FromHours(1);
        var saving = false;

        var title = Field("Title");
        title.FontSize = 15;
        var location = Field("Location (optional)");
        var date = new DatePicker { Date = day, Format = "ddd d MMM yyyy", TextColor = Ui.Text, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center };
        var allDay = new Switch { IsToggled = false, OnColor = Ui.Blue, VerticalOptions = LayoutOptions.Center };
        var from = Clock(start);
        var to = Clock(Min(start + length, LastMinute));
        var calendars = new Picker { Title = "Calendar", TextColor = Ui.Text, TitleColor = Ui.Tertiary, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center };
        var problem = Ui.Label("", 11, color: Ui.Red);
        problem.IsVisible = false;

        // Moving the start carries the end with it, the same length; moving the end changes the length.
        from.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TimePicker.Time) && from.Time is { } f)
                to.Time = Min(f + length, LastMinute);
        };
        to.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TimePicker.Time) && from.Time is { } f && to.Time is { } t && t > f)
                length = t - f;
        };

        var times = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { from, Ui.Label("to", 13, color: Ui.Secondary).CenteredVertically(), to }
        };
        allDay.Toggled += (_, e) => times.IsVisible = !e.Value;

        // Filled in once the store answers; left out where there's no choosing (Windows has the app's own).
        IReadOnlyList<WritableCalendar> writable = [];
        var calendarRow = Row("Calendar", calendars);
        calendarRow.IsVisible = false;
        _ = LoadCalendarsAsync();

        async Task LoadCalendarsAsync()
        {
            writable = await this.calendar.GetWritableCalendarsAsync();
            if (writable.Count == 0)
                return;
            calendars.ItemsSource = new[] { "Default" }.Concat(writable.Select(c => c.Name)).ToList();
            calendars.SelectedIndex = 0;
            calendarRow.IsVisible = true;
        }

        void Problem(string text)
        {
            problem.Text = text;
            problem.IsVisible = true;
        }

        async Task SaveAsync()
        {
            if (saving)
                return;

            var name = title.Text?.Trim();
            if (String.IsNullOrEmpty(name))
            {
                Problem("Give it a title.");
                return;
            }

            var on = date.Date?.Date ?? day;
            var begins = on + (from.Time ?? start);
            var ends = on + (to.Time ?? start + length);
            if (!allDay.IsToggled && ends <= begins)
            {
                Problem("It ends before it starts.");
                return;
            }

            // "Default" is the first item: wherever the system puts new events.
            var picked = calendars.SelectedIndex > 0 ? writable[calendars.SelectedIndex - 1].Id : null;
            saving = true;
            try
            {
                await this.calendar.AddEventAsync(name, location.Text, begins, ends, allDay.IsToggled, picked);
                this.quickEntry.Hide();
            }
            catch (Exception ex)
            {
                Problem($"Couldn't add it: {ex.Message}");
            }
            finally
            {
                saving = false;
            }
        }

        title.Completed += (_, _) => _ = SaveAsync();
        title.Loaded += (_, _) => title.Dispatcher.Dispatch(() => title.Focus());

        var buttons = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.End,
            Children =
            {
                Ui.Chip("Cancel", this.quickEntry.Hide),
                Ui.Chip("Add", () => _ = SaveAsync(), selected: true, symbol: "calendar.badge.plus")
            }
        };

        return new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(18, 16),
            BackgroundColor = Ui.Surface,
            Children =
            {
                Ui.Caption("New Event"),
                title,
                Row("Location", location),
                Row("Day", new HorizontalStackLayout
                {
                    Spacing = 16,
                    Children = { date, Ui.Label("All day", 13, color: Ui.Secondary).CenteredVertically(), allDay }
                }),
                Row("Time", times),
                calendarRow,
                problem,
                buttons
            }
        };
    }

    static readonly TimeSpan LastMinute = new(23, 59, 0);

    static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    static Entry Field(string placeholder) => new()
    {
        Placeholder = placeholder,
        FontSize = 13,
        TextColor = Ui.Text,
        PlaceholderColor = Ui.Tertiary
    };

    static TimePicker Clock(TimeSpan time) => new()
    {
        Time = time,
        Format = "t",
        TextColor = Ui.Text,
        VerticalOptions = LayoutOptions.Center
    };

    /// <summary>A label down the left, the control beside it.</summary>
    static Grid Row(string label, View control) => new()
    {
        ColumnSpacing = 10,
        ColumnDefinitions = [new ColumnDefinition(LabelWidth), new ColumnDefinition(GridLength.Star)],
        Children =
        {
            Ui.Label(label, 13, color: Ui.Secondary).CenteredVertically().At(column: 0),
            control.At(column: 1)
        }
    };
}

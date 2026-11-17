using Shiny;
using Shiny.Calendar;

namespace ShinyNotch.Services;

public sealed record CalendarItem(string Title, DateTime Start, DateTime End, bool AllDay, Color Color, string? Location);

/// <summary>A calendar a new event can go in.</summary>
public sealed record WritableCalendar(string Id, string Name, Color Color);

/// <summary>Upcoming events from every calendar the user has, and new ones added, through Shiny.Calendar.</summary>
[Singleton]
public sealed class CalendarService
{
    static readonly Color DefaultColor = Color.FromArgb("#22D3EE");

    readonly ICalendarStore store;
    readonly IDispatcher dispatcher;
    readonly TimeProvider time;
    int refreshVersion;
    /// <summary>
    /// Access was asked for and given. Windows can't say whether it has been without asking again (it
    /// reports Unknown), so a grant is remembered here.
    /// </summary>
    bool granted;

    public bool IsAuthorized => this.granted || this.store.GetCurrentAccess() == AccessState.Available;

    public bool IsDenied => this.store.GetCurrentAccess() is AccessState.Denied or AccessState.Restricted;

    /// <summary>The platform has no calendar to read: the tab says so, and Settings has nothing to ask.</summary>
    public bool IsSupported => this.store.GetCurrentAccess() != AccessState.NotSupported;

    IReadOnlyList<CalendarItem> fetched = [];
    IReadOnlyList<CalendarItem> selectedEvents = [];
    int selectVersion;

    /// <summary>The day picked in the week strip; null is today, which shows <see cref="Upcoming"/> instead.</summary>
    public DateTime? SelectedDate { get; private set; }

    /// <summary>Every event on <see cref="SelectedDate"/>, past ones included.</summary>
    public IReadOnlyList<CalendarItem> SelectedEvents => this.selectedEvents;

    /// <summary>
    /// What's left of the last query: events drop off here as they end, so the store only needs asking
    /// again when it changes, or now and then for the day moving on.
    /// </summary>
    public IReadOnlyList<CalendarItem> Upcoming
    {
        get
        {
            var now = this.time.GetLocalNow().DateTime;
            return this.fetched.Where(e => e.End > now).Take(8).ToList();
        }
    }

    public event EventHandler? Changed;

    public CalendarService(ICalendarStore store, IDispatcher dispatcher, TimeProvider time)
    {
        this.store = store;
        this.dispatcher = dispatcher;
        this.time = time;

        // Edits made in Calendar.app (or synced in) arrive here, possibly off the main thread.
        this.store.Changed += (_, _) => this.dispatcher.Dispatch(() => _ = this.RefreshAsync());
    }

    public async Task<bool> RequestAccessAsync()
    {
        if (this.IsAuthorized)
            return true;

        try
        {
            // Read and write: events can be added from the Calendar tab.
            this.granted = await this.store.RequestAccess(CalendarAccessType.ReadWrite).ConfigureAwait(true) == AccessState.Available;
            if (this.granted)
                await this.RefreshAsync().ConfigureAwait(true);
            return this.granted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Picks a day to show; today (or null) goes back to the upcoming list.</summary>
    public async Task SelectAsync(DateTime? date)
    {
        var today = this.time.GetLocalNow().Date;
        date = date?.Date == today ? null : date?.Date;
        var version = ++this.selectVersion;
        this.SelectedDate = date;
        this.selectedEvents = [];

        if (date is { } day && this.IsAuthorized)
        {
            try
            {
                var colors = await this.GetColorsAsync().ConfigureAwait(true);
                var events = await this.store
                    .Query()
                    .Between(day, day.AddDays(1))
                    .ToListAsync()
                    .ConfigureAwait(true);

                if (version != this.selectVersion)
                    return;

                this.selectedEvents = events
                    .Select(e => ToItem(e, colors))
                    .Where(e => e.End > day && e.Start < day.AddDays(1))
                    .OrderBy(e => e.AllDay ? 0 : 1)
                    .ThenBy(e => e.Start)
                    .ToList();
            }
            catch
            {
                if (version != this.selectVersion)
                    return;
            }
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The calendars a new event can go in. None on Windows: an app may only write to calendars it made
    /// itself, so new events go into the app's own (see <see cref="AddEventAsync"/>).
    /// </summary>
    public async Task<IReadOnlyList<WritableCalendar>> GetWritableCalendarsAsync()
    {
        if (!this.IsAuthorized || OperatingSystem.IsWindows())
            return [];

        try
        {
            var calendars = await this.store.GetAll().ConfigureAwait(true);
            return calendars
                .Where(c => !c.IsReadOnly && c.Id is not null)
                .Select(c => new WritableCalendar(c.Id!, c.Name, ToColor(c.Color)))
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Adds an event, to <paramref name="calendarId"/> or, when null, wherever the system puts new events
    /// (on Windows, a calendar of the app's own). All day runs midnight to midnight of
    /// <paramref name="start"/>'s day. Throws when the store won't take it.
    /// </summary>
    public async Task AddEventAsync(string title, string? location, DateTime start, DateTime end, bool allDay, string? calendarId)
    {
        if (allDay)
        {
            start = start.Date;
            end = start.AddDays(1);
        }

        await this.store.CreateEvent(new CalendarEvent(title, new DateTimeOffset(start), new DateTimeOffset(end))
        {
            Location = String.IsNullOrWhiteSpace(location) ? null : location,
            IsAllDay = allDay,
            CalendarId = calendarId
        }).ConfigureAwait(true);

        // The store's own change notice comes too, but not on every platform.
        await this.RefreshAsync().ConfigureAwait(true);
    }

    async Task<Dictionary<string, Color>> GetColorsAsync()
    {
        var calendars = await this.store.GetAll().ConfigureAwait(true);
        return calendars
            .Where(c => c.Id is not null)
            .ToDictionary(c => c.Id!, c => ToColor(c.Color));
    }

    static CalendarItem ToItem(CalendarEvent e, Dictionary<string, Color> colors) => new(
        String.IsNullOrWhiteSpace(e.Title) ? "(No title)" : e.Title,
        e.Start.LocalDateTime,
        e.End.LocalDateTime,
        e.IsAllDay,
        e.CalendarId is not null && colors.TryGetValue(e.CalendarId, out var color) ? color : DefaultColor,
        String.IsNullOrWhiteSpace(e.Location) ? null : e.Location
    );

    /// <summary>The rest of today and all of tomorrow.</summary>
    public async Task RefreshAsync()
    {
        if (!this.IsAuthorized)
            return;

        // A newer refresh can start while this one awaits; only the latest gets to publish.
        var version = ++this.refreshVersion;
        var now = this.time.GetLocalNow().DateTime;

        Dictionary<string, Color> colors;
        IReadOnlyList<CalendarEvent> events;
        try
        {
            colors = await this.GetColorsAsync().ConfigureAwait(true);
            events = await this.store
                .Query()
                .Between(now, now.Date.AddDays(2))
                .ToListAsync()
                .ConfigureAwait(true);
        }
        catch
        {
            return;
        }

        if (version != this.refreshVersion)
            return;

        this.fetched = events
            .Select(e => ToItem(e, colors))
            .Where(e => e.End > now)
            .OrderBy(e => e.AllDay ? 0 : 1)
            .ThenBy(e => e.Start)
            .Take(24)
            .ToList();

        // A store change can touch the picked day too; re-reading it raises Changed for both.
        if (this.SelectedDate is { } selected)
            await this.SelectAsync(selected).ConfigureAwait(true);
        else
            this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The next timed event starting within <paramref name="window"/>, for the peek.</summary>
    public CalendarItem? StartingWithin(TimeSpan window)
    {
        var now = this.time.GetLocalNow().DateTime;
        return this.fetched.FirstOrDefault(e => !e.AllDay && e.Start > now && e.Start - now <= window);
    }

    static Color ToColor(string? hex)
        => !String.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var color) ? color : DefaultColor;
}

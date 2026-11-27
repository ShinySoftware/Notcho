using Shiny;
using Shiny.Maui.Controls.Desktop.Notch;
using Notcho.Platform;
using Notcho.Services;
using Notcho.Views;

namespace Notcho;

/// <summary>
/// The notches: one <see cref="NotchController"/> for each display the notch is on (the primary display
/// unless Settings picks others), made and let go of as displays come and go or the choice changes. And
/// what's done once however many there are: starting the shared services and refreshing them on a clock.
/// </summary>
[Singleton]
public sealed class Notches
{
    /// <summary>A display change arrives as a burst of notifications; act once it settles.</summary>
    static readonly TimeSpan ScreensSettleDelay = TimeSpan.FromMilliseconds(400);
    /// <summary>The store says when events change; this only catches the day moving on.</summary>
    static readonly TimeSpan CalendarRefreshEvery = TimeSpan.FromMinutes(15);
    /// <summary>The forecast refreshes itself when stale (20 minutes); asking every five is cheap.</summary>
    static readonly TimeSpan WeatherCheckEvery = TimeSpan.FromMinutes(5);

    readonly IServiceProvider services;
    readonly INotchWindowFactory factory;
    readonly AppSettings settings;
    readonly IPlatformShell shell;
    readonly IDispatcher dispatcher;
    readonly TimeProvider time;
    readonly INowPlayingService media;
    readonly SystemControlsService controls;
    readonly ClipboardService clipboard;
    readonly KeepAwakeService awake;
    readonly CalendarService calendar;
    readonly WeatherService weather;
    readonly SportsService sports;
    readonly TasksService tasks;
    readonly ICaptureTab? capture;
    readonly bool hasMirror;
    readonly List<NotchController> notches = [];

    Action? openSettings;
    bool started;
    bool reconciling;
    bool reconcileQueued;
    int screensToken;
    DateTimeOffset nextCalendarRefresh;
    DateTimeOffset nextWeatherCheck;

    public Notches(
        IServiceProvider services,
        INotchWindowFactory factory,
        AppSettings settings,
        IPlatformShell shell,
        IDispatcher dispatcher,
        TimeProvider time,
        INowPlayingService media,
        SystemControlsService controls,
        ClipboardService clipboard,
        KeepAwakeService awake,
        CalendarService calendar,
        WeatherService weather,
        SportsService sports,
        TasksService tasks,
        IEnumerable<MirrorSupport> mirror,
        IEnumerable<ICaptureTab> captures)
    {
        this.services = services;
        this.factory = factory;
        this.settings = settings;
        this.shell = shell;
        this.dispatcher = dispatcher;
        this.time = time;
        this.media = media;
        this.controls = controls;
        this.clipboard = clipboard;
        this.awake = awake;
        this.calendar = calendar;
        this.weather = weather;
        this.sports = sports;
        this.tasks = tasks;
        this.hasMirror = mirror.Any();
        this.capture = captures.FirstOrDefault();
    }

    /// <summary>Opens Settings. Set by the app, which owns the window; every notch's gear calls it.</summary>
    public Action? OpenSettings
    {
        get => this.openSettings;
        set
        {
            this.openSettings = value;
            foreach (var notch in this.notches)
                notch.OpenSettings = value;
        }
    }

    /// <summary>The tabs this platform has, with the capture tool's own name on the Capture tab.</summary>
    public IReadOnlyList<NotchTabInfo> AvailableTabs => NotchController.Available(this.capture, this.hasMirror);

    /// <summary>The displays connected now, the primary first.</summary>
    public IReadOnlyList<NotchDisplay> Displays => this.factory.GetDisplays();

    /// <summary>Displays came or went, and the notches have caught up. Settings lists them again.</summary>
    public event EventHandler? DisplaysChanged;

    /// <summary>
    /// The connected displays the notch is on: those picked in Settings, or the primary display when none
    /// of them is connected (or none was ever picked), so there's always a notch somewhere.
    /// </summary>
    public static IReadOnlyList<NotchDisplay> Chosen(IReadOnlyList<NotchDisplay> displays, IReadOnlyList<string> picked)
    {
        var chosen = displays.Where(d => picked.Contains(d.Id)).ToList();
        if (chosen.Count > 0)
            return chosen;
        return displays.Where(d => d.IsPrimary).Take(1).ToList();
    }

    public async Task StartAsync()
    {
        if (this.started)
            return;
        this.started = true;

        this.media.Start();
        this.controls.Start();
        this.clipboard.Start();
        this.awake.StartIfAskedAtLaunch();
        _ = this.calendar.RefreshAsync();
        _ = this.weather.RefreshAsync();
        _ = this.sports.RefreshAsync();
        var now = this.time.GetUtcNow();
        this.nextCalendarRefresh = now + CalendarRefreshEvery;
        this.nextWeatherCheck = now + WeatherCheckEvery;
        this.ScheduleClock();

        this.shell.ScreensChanged += (_, _) => this.OnScreensChanged();
        this.settings.Changed += (_, key) =>
        {
            if (key == nameof(AppSettings.NotchDisplays))
                _ = this.ReconcileAsync();
        };

        await this.ReconcileAsync();
    }

    /// <summary>Folds every open panel back to its peek, pinned or not (see <see cref="NotchController.FoldAway"/>).</summary>
    public void FoldAway()
    {
        foreach (var notch in this.notches)
            notch.FoldAway();
    }

    /// <summary>Opens the primary display's panel (or the first notch's) on a tab, pinned.</summary>
    public void OpenPinned(NotchTab tab)
    {
        var primary = this.Displays.FirstOrDefault(d => d.IsPrimary)?.Id;
        var notch = this.notches.FirstOrDefault(n => n.DisplayId == primary) ?? this.notches.FirstOrDefault();
        notch?.OpenPinned(tab);
    }

    void OnScreensChanged()
    {
        var token = ++this.screensToken;
        this.dispatcher.DispatchDelayed(ScreensSettleDelay, async () =>
        {
            if (token != this.screensToken)
                return;
            await this.ReconcileAsync();
            this.DisplaysChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// A notch on every chosen display and nowhere else: the ones on displays that went, or were turned
    /// off, close; the ones on displays newly chosen start. One at a time, like a render.
    /// </summary>
    async Task ReconcileAsync()
    {
        if (this.reconciling)
        {
            this.reconcileQueued = true;
            return;
        }

        this.reconciling = true;
        try
        {
            do
            {
                this.reconcileQueued = false;
                await this.ReconcileOnceAsync();
            }
            while (this.reconcileQueued);
        }
        finally
        {
            this.reconciling = false;
        }
    }

    async Task ReconcileOnceAsync()
    {
        var displays = this.Displays;
        // No display listed at all (a platform that can't say): one notch on the primary, as before.
        IReadOnlyList<string?> wanted = displays.Count == 0
            ? [null]
            : Chosen(displays, this.settings.NotchDisplays).Select(d => (string?)d.Id).ToList();

        var leaving = this.notches.Where(n => !wanted.Contains(n.DisplayId)).ToList();
        foreach (var notch in leaving)
        {
            this.notches.Remove(notch);
            notch.Dispose();
        }

        var arriving = wanted.Where(id => this.notches.All(n => n.DisplayId != id)).ToList();
        foreach (var id in arriving)
        {
            var notch = ActivatorUtilities.CreateInstance<NotchController>(this.services, this.factory.Create(id));
            notch.OpenSettings = this.openSettings;
            this.notches.Add(notch);
            await notch.StartAsync();
        }
    }

    /// <summary>Once a minute, whatever the notches are doing: the things that only need refreshing once for all of them.</summary>
    void ScheduleClock()
    {
        this.dispatcher.DispatchDelayed(TimeSpan.FromMinutes(1), () =>
        {
            var now = this.time.GetUtcNow();

            // Checked-off tasks clear away a day later; the minute is plenty for that.
            this.tasks.Prune();

            if (now >= this.nextCalendarRefresh)
            {
                this.nextCalendarRefresh = now + CalendarRefreshEvery;
                _ = this.calendar.RefreshAsync();
            }

            if (now >= this.nextWeatherCheck)
            {
                this.nextWeatherCheck = now + WeatherCheckEvery;
                _ = this.weather.RefreshAsync();
            }

            this.ScheduleClock();
        });
    }
}

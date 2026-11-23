using Shiny;
using Notcho.Platform;

namespace Notcho.Services;

/// <summary>
/// Keeps the computer awake until it's stopped, or for a while. The screen stays on too unless Settings
/// says otherwise, and a change there applies to a session already running.
/// </summary>
/// <remarks>
/// Like the countdown, nothing ticks here: one timer is set for the moment a timed session ends, and moved
/// when it is extended or stopped. The notch draws the time left on its own clock while it shows it.
/// </remarks>
[Singleton]
public sealed class KeepAwakeService : IDisposable
{
    readonly IDispatcher dispatcher;
    readonly TimeProvider time;
    readonly IKeepAwake platform;
    readonly AppSettings settings;
    readonly ITimer finish;
    // UTC, so a DST switch or a time zone change doesn't move a session that's running.
    DateTimeOffset endsAt;

    public KeepAwakeService(IDispatcher dispatcher, TimeProvider time, IKeepAwake platform, AppSettings settings)
    {
        this.dispatcher = dispatcher;
        this.time = time;
        this.platform = platform;
        this.settings = settings;
        // The callback comes on a thread-pool thread.
        this.finish = time.CreateTimer(_ => this.dispatcher.Dispatch(this.OnFinish), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        this.settings.Changed += (_, key) =>
        {
            if (key == nameof(AppSettings.AwakeKeepDisplayOn) && this.IsActive)
                this.platform.Hold(this.settings.AwakeKeepDisplayOn);
        };
    }

    public bool IsActive { get; private set; }

    /// <summary>How long the session was asked for (and extended by); null until it's stopped.</summary>
    public TimeSpan? Duration { get; private set; }

    /// <summary>Running until it's stopped, rather than for a while.</summary>
    public bool IsIndefinite => this.IsActive && this.Duration == null;

    /// <summary>When a timed session ends, in the computer's own time zone; null for any other.</summary>
    public DateTimeOffset? EndsAt => this.IsActive && this.Duration != null
        ? TimeZoneInfo.ConvertTime(this.endsAt, this.time.LocalTimeZone)
        : null;

    /// <summary>What's left of a timed session; zero for any other.</summary>
    public TimeSpan Remaining
    {
        get
        {
            if (!this.IsActive || this.Duration == null)
                return TimeSpan.Zero;

            var left = this.endsAt - this.time.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>How much of a timed session is left, 1 to 0; 1 for one that runs until it's stopped.</summary>
    public double Fraction => !this.IsActive ? 0
        : this.Duration is { } duration && duration > TimeSpan.Zero ? this.Remaining / duration
        : 1;

    /// <summary>Started, extended, stopped or run out.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Starts a session, or starts over the one running.</summary>
    /// <param name="duration">How long for; null runs until it's stopped.</param>
    public void Start(TimeSpan? duration)
    {
        this.Duration = duration;
        if (duration is { } d)
            this.endsAt = this.time.GetUtcNow() + d;
        if (!this.IsActive)
            this.platform.Hold(this.settings.AwakeKeepDisplayOn);
        this.IsActive = true;
        this.Arm();
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>More time on a timed session, or a timed session of that long when none is running.</summary>
    public void Add(TimeSpan amount)
    {
        if (!this.IsActive)
        {
            this.Start(amount);
            return;
        }
        if (this.Duration is not { } duration)
            return;

        this.endsAt += amount;
        this.Duration = duration + amount;
        this.Arm();
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>On until it's stopped, or off.</summary>
    public void Toggle()
    {
        if (this.IsActive)
            this.Stop();
        else
            this.Start(null);
    }

    public void Stop()
    {
        if (!this.IsActive)
            return;

        this.IsActive = false;
        this.Duration = null;
        this.platform.Release();
        this.Arm();
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Settings asks for the computer to stay awake for as long as the app runs.</summary>
    public void StartIfAskedAtLaunch()
    {
        if (this.settings.AwakeOnLaunch && !this.IsActive)
            this.Start(null);
    }

    /// <summary>Sets the one timer for a timed session's end, or stops it otherwise.</summary>
    void Arm() => this.finish.Change(this.IsActive && this.Duration != null ? this.Remaining : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    void OnFinish()
    {
        if (!this.IsActive || this.Duration == null)
            return;

        // Woken early (the timer and the wall clock can disagree slightly): set it again.
        if (this.Remaining > TimeSpan.Zero)
        {
            this.Arm();
            return;
        }

        this.Stop();
    }

    /// <summary>A session's length the way the presets name it: "15 min", "1 hour", "2 hours".</summary>
    public static string Name(TimeSpan t)
        => t.TotalHours >= 1 ? $"{t.TotalHours:0.#} {(t.TotalHours == 1 ? "hour" : "hours")}" : $"{t.TotalMinutes:0} min";

    public void Dispose()
    {
        this.finish.Dispose();
        if (this.IsActive)
            this.platform.Release();
    }
}

using Microsoft.Win32.SafeHandles;
using Shiny;
using ShinyNotch.Platform;
using Tmds.DBus.Protocol;

namespace ShinyNotch.Linux;

/// <summary>
/// Two holds, both over D-Bus. logind's sleep inhibitor (<c>systemd-inhibit --list</c> shows it) stops the
/// computer suspending, the desktop's idle suspend included; it lasts as long as the file descriptor logind
/// hands back stays open. For the screen, the desktop's own idle inhibitor: the freedesktop ScreenSaver
/// interface (KDE, Xfce and most others), or GNOME's session manager. Both are let go when the app's bus
/// connection closes, so quitting, however it happens, releases everything.
/// </summary>
[Singleton]
sealed class LinuxKeepAwake : IKeepAwake
{
    const string App = "Shiny Notch";
    const string Reason = "Keeping the computer awake";
    /// <summary>GNOME's session manager: inhibit suspending (4) and the session going idle (8).</summary>
    const uint GnomeSuspendAndIdle = 4 | 8;

    readonly Lock gate = new();
    /// <summary>Bumped by every hold and release, so a hold still being granted when it's replaced is let go.</summary>
    int generation;
    SafeFileHandle? sleepLock;
    /// <summary>Which interface granted the screen hold, and its cookie.</summary>
    (bool Gnome, uint Cookie)? screenLock;

    public void Hold(bool display)
    {
        int ours;
        lock (this.gate)
            ours = ++this.generation;
        _ = this.HoldAsync(ours, display);
    }

    public void Release()
    {
        lock (this.gate)
            this.generation++;
        // A hold still on its way sees the new generation and lets go of what it's granted.
        this.LetGo();
    }

    async Task HoldAsync(int ours, bool display)
    {
        var sleep = await InhibitSleepAsync().ConfigureAwait(false);
        var screen = display ? await InhibitScreenAsync().ConfigureAwait(false) : null;

        lock (this.gate)
        {
            if (ours == this.generation)
            {
                // The old locks go only once the new ones are held, so there's no gap to sleep in.
                var oldSleep = this.sleepLock;
                var oldScreen = this.screenLock;
                this.sleepLock = sleep;
                this.screenLock = screen;
                oldSleep?.Dispose();
                if (oldScreen is { } old)
                    _ = UninhibitScreenAsync(old);
                return;
            }
        }

        // Replaced or released while it was being granted.
        sleep?.Dispose();
        if (screen is { } granted)
            _ = UninhibitScreenAsync(granted);
    }

    void LetGo()
    {
        SafeFileHandle? oldSleep;
        (bool, uint)? oldScreen;
        lock (this.gate)
        {
            oldSleep = this.sleepLock;
            oldScreen = this.screenLock;
            this.sleepLock = null;
            this.screenLock = null;
        }
        oldSleep?.Dispose();
        if (oldScreen is { } old)
            _ = UninhibitScreenAsync(old);
    }

    static async Task<SafeFileHandle?> InhibitSleepAsync()
    {
        try
        {
            var connection = DBusConnection.System;
            return await connection.CallMethodAsync(
                DBus.Call(connection, "org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.login1.Manager", "Inhibit", "ssss", w =>
                {
                    w.WriteString("sleep");
                    w.WriteString(App);
                    w.WriteString(Reason);
                    w.WriteString("block");
                }),
                static (Message reply, object? _) => reply.GetBodyReader().ReadHandle<SafeFileHandle>()
            ).ConfigureAwait(false);
        }
        catch
        {
            // No logind: the screen hold, if any, is all there is.
            return null;
        }
    }

    static async Task<(bool Gnome, uint Cookie)?> InhibitScreenAsync()
    {
        var connection = DBusConnection.Session;
        try
        {
            var cookie = await connection.CallMethodAsync(
                DBus.Call(connection, "org.freedesktop.ScreenSaver", "/org/freedesktop/ScreenSaver", "org.freedesktop.ScreenSaver", "Inhibit", "ss", w =>
                {
                    w.WriteString(App);
                    w.WriteString(Reason);
                }),
                static (Message reply, object? _) => reply.GetBodyReader().ReadUInt32()
            ).ConfigureAwait(false);
            return (false, cookie);
        }
        catch
        {
            // GNOME doesn't implement the freedesktop interface's Inhibit; its session manager does the same.
        }

        try
        {
            var cookie = await connection.CallMethodAsync(
                DBus.Call(connection, "org.gnome.SessionManager", "/org/gnome/SessionManager", "org.gnome.SessionManager", "Inhibit", "susu", w =>
                {
                    w.WriteString(App);
                    w.WriteUInt32(0);
                    w.WriteString(Reason);
                    w.WriteUInt32(GnomeSuspendAndIdle);
                }),
                static (Message reply, object? _) => reply.GetBodyReader().ReadUInt32()
            ).ConfigureAwait(false);
            return (true, cookie);
        }
        catch
        {
            // Neither: the screen goes dark on the desktop's schedule, though the computer stays awake.
            return null;
        }
    }

    static async Task UninhibitScreenAsync((bool Gnome, uint Cookie) held)
    {
        try
        {
            var connection = DBusConnection.Session;
            var message = held.Gnome
                ? DBus.Call(connection, "org.gnome.SessionManager", "/org/gnome/SessionManager", "org.gnome.SessionManager", "Uninhibit", "u", w => w.WriteUInt32(held.Cookie))
                : DBus.Call(connection, "org.freedesktop.ScreenSaver", "/org/freedesktop/ScreenSaver", "org.freedesktop.ScreenSaver", "UnInhibit", "u", w => w.WriteUInt32(held.Cookie));
            await connection.CallMethodAsync(message).ConfigureAwait(false);
        }
        catch
        {
            // The desktop went away, and the hold with it.
        }
    }
}

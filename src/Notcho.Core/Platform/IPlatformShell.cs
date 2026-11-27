namespace Notcho.Platform;

/// <summary>
/// Where the app meets the desktop it runs on: its windows, quitting, the system's own settings, and
/// the displays. One per head.
/// </summary>
public interface IPlatformShell
{
    /// <summary>
    /// Takes a window off screen without closing it. The app starts in the notch, so the first window
    /// MAUI insists on (Settings) is hidden as soon as it exists.
    /// </summary>
    void Hide(Window window);

    /// <summary>Brings a window to the front, centred, and the app forward with it.</summary>
    void Present(Window window);

    void Quit();

    /// <summary>Displays were plugged in, removed or rescaled. May be raised in a burst.</summary>
    event EventHandler? ScreensChanged;

    /// <summary>A short sound for a countdown ending.</summary>
    void PlayAlert();

    /// <summary>Opens the system's calendar privacy settings, or false when it has none to open.</summary>
    bool OpenCalendarPrivacySettings();

    /// <summary>Where the user manages login items, for Settings to point at ("System Settings ▸ General ▸ Login Items").</summary>
    string LoginItemsLocation { get; }

    /// <summary>
    /// Lifts another of the app's windows (by title) above the notch, which sits over almost everything:
    /// the quick entry popup tasks are typed into would otherwise open underneath the panel. Nothing to do
    /// where the platform stacks the window shown last on top.
    /// </summary>
    void RaiseAboveNotch(string windowTitle) { }

    /// <summary>
    /// Just after the notch's content was swapped for wings of uneven width (see <c>PeekView</c>). The notch
    /// window is always centred on the camera, so those wings are drawn in a window as wide as the wider one
    /// on both sides, black over only part of it. Where the old content is frozen into an image that
    /// collapses with the window (macOS), it collapses to that even width and only then fades, so a panel
    /// closing onto a narrow side lost that side's extra black after the rest had closed. This trims the
    /// frozen image in step with the morph instead, down to <paramref name="wings"/>' own outline. Nothing
    /// to do where no frozen image is left behind.
    /// </summary>
    /// <param name="notch">The notch window that was swapped: with a notch on more than one display, there's one each.</param>
    /// <param name="height">The new content's height: where the window's morph is heading.</param>
    /// <param name="cornerRadius">The wings' bottom corners.</param>
    void TrimMorphToWings(Window? notch, Views.Wings wings, double height, double cornerRadius) { }
}

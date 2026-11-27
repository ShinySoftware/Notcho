using System.Diagnostics;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Shiny;
using Notcho.Platform;
using Notcho.Views;

namespace Notcho.MacOS;

[Singleton]
sealed class MacPlatformShell : IPlatformShell
{
    NSObject? screensObserver;
    EventHandler? screensChanged;

    public void Hide(Window window)
    {
        if (window.Handler?.PlatformView is NSWindow native)
            native.OrderOut(null);
    }

    public void Present(Window window)
    {
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
        if (window.Handler?.PlatformView is NSWindow native)
        {
            native.Center();
            native.MakeKeyAndOrderFront(null);
        }
    }

    public void Quit() => NSApplication.SharedApplication.Terminate(null);

    public event EventHandler? ScreensChanged
    {
        add
        {
            this.screensChanged += value;
            this.screensObserver ??= NSNotificationCenter.DefaultCenter.AddObserver(
                NSApplication.DidChangeScreenParametersNotification,
                _ => this.screensChanged?.Invoke(this, EventArgs.Empty)
            );
        }
        remove => this.screensChanged -= value;
    }

    public void PlayAlert() => NSSound.FromName("Glass")?.Play();

    public bool OpenCalendarPrivacySettings()
        => NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_Calendars"));

    public string LoginItemsLocation => "System Settings ▸ General ▸ Login Items";

    /// <summary>One level over the notch's (Status), so the window is drawn on top of the panel.</summary>
    public void RaiseAboveNotch(string windowTitle)
    {
        foreach (var window in Application.Current?.Windows ?? [])
        {
            if (window.Title == windowTitle && window.Handler?.PlatformView is NSWindow native)
                native.Level = NSWindowLevel.Status + 1;
        }
    }

    /// <summary>
    /// Masks the frozen image Shiny's notch window leaves over a swap - a click-through child window holding
    /// an image view - down to the wings' outline, a step every frame. How far along the morph is comes from
    /// the frozen window's own frame as AppKit animates it, so the trim keeps pace with the window rather
    /// than with a clock of its own.
    /// </summary>
    public void TrimMorphToWings(Window? notchWindow, Wings wings, double height, double cornerRadius)
    {
        var notch = notchWindow?.Handler?.PlatformView as NSWindow;
        var frozen = notch?.ChildWindows?.FirstOrDefault(w => w.IgnoresMouseEvents && w.ContentView is NSImageView);
        if (frozen?.ContentView is not { } image)
            return;

        image.WantsLayer = true;
        if (image.Layer is not { } layer)
            return;

        // Opaque where the frozen image still shows, with the wings' rounded bottom corners.
        var mask = new CALayer
        {
            BackgroundColor = NSColor.Black.CGColor,
            CornerRadius = (nfloat)cornerRadius,
            MaskedCorners = CACornerMask.MinXMinYCorner | CACornerMask.MaxXMinYCorner
        };
        layer.Mask = mask;

        var from = frozen.Frame.Size;
        var started = Stopwatch.GetTimestamp();
        NSTimer? timer = null;

        void Step()
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            // Thawed: the morph is over, or another swap took its place.
            if (frozen.ParentWindow == null || !frozen.IsVisible || elapsed > NotchoHosting.MorphDuration * 2)
            {
                timer?.Invalidate();
                return;
            }

            // Closing, the height changes most; between peeks of the same size, only the clock does.
            var now = frozen.Frame.Size;
            var progress = Math.Abs(from.Height - height) >= 1 ? (from.Height - now.Height) / (from.Height - height)
                : Math.Abs(from.Width - wings.Width) >= 1 ? (from.Width - now.Width) / (from.Width - wings.Width)
                : elapsed / NotchoHosting.MorphDuration;
            // The morph overshoots a little before it settles, and the trim with it.
            progress = Math.Clamp(progress, 0, 1.2);
            var left = progress * wings.LeftInset;
            var right = progress * wings.RightInset;

            CATransaction.Begin();
            CATransaction.DisableActions = true;
            mask.Frame = new CGRect((nfloat)left, 0, (nfloat)Math.Max(0, now.Width - left - right), now.Height);
            CATransaction.Commit();
        }

        Step();
        timer = NSTimer.CreateRepeatingTimer(1.0 / 120, _ => Step());
        // Common modes, so it keeps stepping while AppKit runs the window's animation.
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
    }
}

using System.Runtime.InteropServices;
using Foundation;
using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.MacOS;

/// <summary>
/// An IOKit power assertion, as <c>caffeinate</c> and Amphetamine take: "prevent idle display sleep" keeps
/// the screen on (and so the Mac), "prevent idle system sleep" only the Mac. macOS lets go of it when the
/// app quits, however it quits, and lists it in <c>pmset -g assertions</c> under the reason given here.
/// </summary>
[Singleton]
sealed class MacKeepAwake : IKeepAwake
{
    const string IOKitLib = "/System/Library/Frameworks/IOKit.framework/IOKit";
    const uint LevelOn = 255;
    const string Reason = "Shiny Notch is keeping this Mac awake";

    [DllImport(IOKitLib)]
    static extern int IOPMAssertionCreateWithName(IntPtr type, uint level, IntPtr name, out uint id);

    [DllImport(IOKitLib)]
    static extern int IOPMAssertionRelease(uint id);

    uint? held;

    public void Hold(bool display)
    {
        // Taken before the old one goes, so the Mac is never left free to sleep in between.
        using var type = new NSString(display ? "PreventUserIdleDisplaySleep" : "PreventUserIdleSystemSleep");
        using var name = new NSString(Reason);
        var taken = IOPMAssertionCreateWithName(type.Handle, LevelOn, name.Handle, out var id) == 0 ? id : (uint?)null;
        this.Release();
        this.held = taken;
    }

    public void Release()
    {
        if (this.held is { } id)
            IOPMAssertionRelease(id);
        this.held = null;
    }
}

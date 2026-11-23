using System.Runtime.InteropServices;
using Shiny;
using Notcho.Platform;

namespace Notcho.Windows;

/// <summary>
/// <c>SetThreadExecutionState</c> with <c>ES_CONTINUOUS</c>, which holds until it's changed again or the
/// thread ends. Always called on the UI thread (see <see cref="IKeepAwake"/>), which lives as long as the app,
/// so Windows lets go of it when the app quits. <c>powercfg /requests</c> lists it.
/// </summary>
[Singleton]
sealed partial class WindowsKeepAwake : IKeepAwake
{
    const uint Continuous = 0x80000000;
    const uint SystemRequired = 0x00000001;
    const uint DisplayRequired = 0x00000002;

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);

    public void Hold(bool display)
        => SetThreadExecutionState(Continuous | SystemRequired | (display ? DisplayRequired : 0));

    public void Release() => SetThreadExecutionState(Continuous);
}

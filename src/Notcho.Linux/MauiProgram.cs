using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Shiny;
using Notcho.Linux;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent.Gtk;
#endif

namespace Notcho;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppLinuxGtk4<App>()
            .AddLinuxGtk4Essentials()
            .UseNotcho();
        // Gradient backgrounds for the notch's surfaces.
        NotchSurfaceHandler.Register(builder);

        // GeoClue for the weather's "where I am": after UseNotcho's AddGps, which has no GPS for plain net11.0.
        // The desktop id is what the location agent shows and remembers its answer by: the app's .desktop name.
        builder.Services.AddLinuxGps(o => o.DesktopId = "net.shiny.notcho");

        // Before anything else from Shiny.Audio: PulseAudio/PipeWire capture and playback for voice notes.
        builder.Services.AddLinuxAudio();
        // No speech-to-text: Linux has no OS recognizer, so the teleprompter doesn't offer to follow a voice.

        // Everything marked [Singleton] in this head: the Platform implementations, and a calendar store
        // that says there's no calendar (Shiny.Calendar has none for plain net11.0).
        builder.Services.AddPlatformServices();

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.AddMauiDevFlowAgent();
#endif
        return builder.Build();
    }
}

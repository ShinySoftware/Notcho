using System.Globalization;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

namespace Notcho.Linux;

/// <summary>
/// The GTK head's Border, with gradient backgrounds: the platform's own paints solid colours only, and
/// leaves anything else clear. A gradient goes on as a CSS background image of its own.
/// </summary>
sealed class NotchSurfaceHandler : BorderHandler
{
    public static MauiAppBuilder Register(MauiAppBuilder builder)
    {
        Mapper.AppendToMapping("Background", (handler, _) => (handler as NotchSurfaceHandler)?.Paint());
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Border, NotchSurfaceHandler>());
        return builder;
    }

    void Paint()
    {
        var css = this.VirtualView?.Background is LinearGradientPaint { GradientStops.Length: > 0 } paint ? Css(paint) : null;
        this.UpdateCss(this.PlatformView, css, "NotchoGradient");
    }

    static string Css(LinearGradientPaint paint)
    {
        // CSS angles start at the top and turn clockwise: from the start point towards the end point.
        var dx = paint.EndPoint.X - paint.StartPoint.X;
        var dy = paint.EndPoint.Y - paint.StartPoint.Y;
        var angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
        var stops = paint.GradientStops
            .OrderBy(s => s.Offset)
            .Select(s => string.Create(CultureInfo.InvariantCulture, $"rgba({(int)(s.Color.Red * 255)},{(int)(s.Color.Green * 255)},{(int)(s.Color.Blue * 255)},{s.Color.Alpha}) {s.Offset * 100}%"));
        return string.Create(CultureInfo.InvariantCulture, $"background-image: linear-gradient({angle}deg, {string.Join(", ", stops)}); ");
    }
}

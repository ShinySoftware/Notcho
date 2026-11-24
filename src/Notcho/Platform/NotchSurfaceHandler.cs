using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Platforms.MacOS.Handlers;
using Notcho.Views;

namespace Notcho.MacOS;

/// <summary>
/// The AppKit head's Border, with what the notch's surfaces need and the platform's own lacks: a gradient
/// background (it paints solid colours only, and anything else clear), and frosted glass - a blur of
/// whatever is behind the notch window, under the Border's content and cut to its shape.
/// </summary>
sealed class NotchSurfaceHandler : BorderHandler
{
    const string GradientName = "NotchoGradient";

    NSVisualEffectView? frost;
    CGSize maskSize;

    public static MauiAppBuilder Register(MauiAppBuilder builder)
    {
        // After the platform's own: it has already cleared the layer when the background isn't solid.
        Mapper.AppendToMapping("Background", (handler, _) => (handler as NotchSurfaceHandler)?.Paint());
        Mapper.AppendToMapping(Frost.FrostedProperty.PropertyName, (handler, _) => (handler as NotchSurfaceHandler)?.Paint());
        Mapper.AppendToMapping("Shape", (handler, _) => (handler as NotchSurfaceHandler)?.Reshape());
        // A new content view takes every subview out with the old one, the blur too.
        Mapper.AppendToMapping("Content", (handler, _) => (handler as NotchSurfaceHandler)?.UpdateFrost());
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Border, NotchSurfaceHandler>());
        return builder;
    }

    public override void PlatformArrange(Rect rect)
    {
        base.PlatformArrange(rect);
        var view = this.PlatformView;
        if (view?.Layer == null)
            return;

        // Without this, a resized layer animates to its new size, behind the notch's own morph.
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        var gradient = view.Layer.Sublayers?.FirstOrDefault(l => l.Name == GradientName);
        if (gradient != null)
            gradient.Frame = view.Layer.Bounds;
        CATransaction.Commit();

        if (this.frost != null)
        {
            this.frost.Frame = view.Bounds;
            this.Mask();
        }
    }

    void Paint()
    {
        var layer = this.PlatformView?.Layer;
        if (layer == null)
            return;

        layer.Sublayers?.FirstOrDefault(l => l.Name == GradientName)?.RemoveFromSuperLayer();
        if (this.VirtualView?.Background is LinearGradientPaint { GradientStops.Length: > 0 } paint)
        {
            layer.BackgroundColor = null;
            var stops = paint.GradientStops.OrderBy(s => s.Offset).ToArray();
            layer.InsertSublayer(new CAGradientLayer
            {
                Name = GradientName,
                Frame = layer.Bounds,
                StartPoint = new CGPoint(paint.StartPoint.X, paint.StartPoint.Y),
                EndPoint = new CGPoint(paint.EndPoint.X, paint.EndPoint.Y),
                Colors = stops.Select(s => ToCGColor(s.Color)).ToArray(),
                Locations = stops.Select(s => new NSNumber(s.Offset)).ToArray()
            }, 0);
        }
        this.UpdateFrost();
    }

    void Reshape()
    {
        this.maskSize = CGSize.Empty;
        this.Mask();
    }

    void UpdateFrost()
    {
        var view = this.PlatformView;
        if (view == null)
            return;

        var frosted = this.VirtualView is BindableObject bindable && Frost.GetFrosted(bindable);
        if (!frosted)
        {
            this.frost?.RemoveFromSuperview();
            this.frost = null;
            return;
        }

        // The notch window is never key, so the blur is told to stay on rather than follow the window.
        this.frost ??= new FrostView
        {
            Material = NSVisualEffectMaterial.HudWindow,
            BlendingMode = NSVisualEffectBlendingMode.BehindWindow,
            State = NSVisualEffectState.Active,
            Appearance = NSAppearance.GetAppearance(NSAppearance.NameDarkAqua),
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
        };
        if (this.frost.Superview != view)
            view.AddSubview(this.frost, NSWindowOrderingMode.Below, null);
        this.frost.Frame = view.Bounds;
        this.maskSize = CGSize.Empty;
        this.Mask();
    }

    /// <summary>
    /// Cuts the blur to the Border's shape. A blur of what's behind the window is drawn by the window server,
    /// which goes by the effect view's mask image rather than the layer mask the Border clips its content with.
    /// </summary>
    void Mask()
    {
        var view = this.PlatformView;
        var size = view?.Bounds.Size ?? CGSize.Empty;
        if (this.frost == null || size.Width <= 0 || size.Height <= 0 || size == this.maskSize)
            return;

        this.maskSize = size;
        var outline = this.VirtualView?.Shape?.PathForBounds(new Rect(0, 0, size.Width, size.Height))?.GetFlattenedPath();
        if (outline == null)
        {
            // No shape: the whole rectangle. The binding says non-null, but AppKit takes nil for no mask.
            this.frost.MaskImage = null!;
            return;
        }

        // Flipped, so the outline's top-left origin lands where the Border's (also flipped) has it.
        this.frost.MaskImage = NSImage.ImageWithSize(size, true, _ =>
        {
            var context = NSGraphicsContext.CurrentContext?.CGContext;
            if (context == null)
                return false;
            context.AddPath(ToCGPath(outline));
            context.SetFillColor(new CGColor(0, 0, 0, 1));
            context.FillPath();
            return true;
        });
    }

    /// <summary>A flattened path: moves, lines and closes only.</summary>
    static CGPath ToCGPath(PathF flat)
    {
        var path = new CGPath();
        var point = 0;
        foreach (var operation in flat.SegmentTypes)
        {
            if (operation == PathOperation.Close)
                path.CloseSubpath();
            else if (point < flat.Count)
            {
                var p = flat[point++];
                if (operation == PathOperation.Move)
                    path.MoveToPoint(p.X, p.Y);
                else
                    path.AddLineToPoint(p.X, p.Y);
            }
        }
        return path;
    }

    static CGColor ToCGColor(Color c) => new(c.Red, c.Green, c.Blue, c.Alpha);

    /// <summary>Only a backdrop: the pointer goes to the content over it, or to the Border it sits in.</summary>
    sealed class FrostView : NSVisualEffectView
    {
        public override NSView? HitTest(CGPoint point) => null;
    }
}

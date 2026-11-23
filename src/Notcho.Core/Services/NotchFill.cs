namespace Notcho.Services;

public enum NotchFillKind
{
    /// <summary>Black, like the camera cut-out it grows out of. The default.</summary>
    Black,
    Color,
    /// <summary>From one colour at the top to another at the bottom.</summary>
    Gradient
}

/// <summary>
/// What a notch surface is painted with: the closed notch (the peek and the volume/brightness HUD) and the
/// open panel each have one, set in Settings. Gradients only run top to bottom: the open panel is built from
/// pieces (the closed notch it hangs from, the curves either side, the card), and a vertical gradient is the
/// one that lines up across all of them, since they all start at the top edge.
/// </summary>
public sealed record NotchFill(NotchFillKind Kind, Color From, Color To)
{
    public static readonly NotchFill Black = new(NotchFillKind.Black, Colors.Black, Colors.Black);

    /// <summary>The colours Settings offers. Deep enough that the notch's white text stays readable.</summary>
    public static readonly IReadOnlyList<(string Name, Color Color)> Swatches =
    [
        ("Black", Colors.Black),
        ("Graphite", Color.FromArgb("#3A3A3C")),
        ("Midnight", Color.FromArgb("#0B1D3A")),
        ("Ocean", Color.FromArgb("#0A5BC4")),
        ("Turquoise", Color.FromArgb("#0E8A80")),
        ("Guacamole", Color.FromArgb("#4C7F14")),
        ("Nacho Cheese", Color.FromArgb("#D99A0B")),
        ("Pumpkin", Color.FromArgb("#D9590B")),
        ("Salsa", Color.FromArgb("#C62A1F")),
        ("Raspberry", Color.FromArgb("#B0245A")),
        ("Grape", Color.FromArgb("#6A2BB0")),
        ("Refried Bean", Color.FromArgb("#4A2412"))
    ];

    /// <summary>The colour along the top edge, where the notch meets the menu bar.</summary>
    public Color Top => this.Kind == NotchFillKind.Black ? Colors.Black : this.From;

    public Color Bottom => this.Kind == NotchFillKind.Gradient ? this.To : this.Top;

    /// <summary>The colour <paramref name="fraction"/> of the way down: for a piece laid over the fill, like a fade.</summary>
    public Color At(double fraction)
    {
        var t = (float)Math.Clamp(fraction, 0, 1);
        var (a, b) = (this.Top, this.Bottom);
        return new Color(a.Red + (b.Red - a.Red) * t, a.Green + (b.Green - a.Green) * t, a.Blue + (b.Blue - a.Blue) * t);
    }

    public Brush ToBrush() => this.Kind == NotchFillKind.Gradient
        ? new LinearGradientBrush([new GradientStop(this.From, 0), new GradientStop(this.To, 1)], new Point(0, 0), new Point(0, 1))
        : new SolidColorBrush(this.Top);

    /// <summary>The same fill with its own top edge only: for a piece that sits wholly in the top band.</summary>
    public Brush TopBrush() => new SolidColorBrush(this.Top);

    public string Serialize() => $"{this.Kind}|{this.From.ToArgbHex()}|{this.To.ToArgbHex()}";

    /// <summary>Anything unreadable is black, as if it had never been set.</summary>
    public static NotchFill Parse(string? text)
    {
        var parts = (text ?? "").Split('|');
        if (parts.Length != 3 || !Enum.TryParse<NotchFillKind>(parts[0], out var kind))
            return Black;
        if (!TryColor(parts[1], out var from) || !TryColor(parts[2], out var to))
            return Black;
        return kind == NotchFillKind.Black ? Black : new NotchFill(kind, from, to);
    }

    /// <summary>A colour typed as #RGB, #RRGGBB or #AARRGGBB, with or without the #.</summary>
    public static bool TryColor(string? text, out Color color)
    {
        var hex = (text ?? "").Trim().TrimStart('#');
        var valid = hex.Length is 3 or 6 or 8 && hex.All(Uri.IsHexDigit);
        color = valid ? Color.FromArgb("#" + hex) : Colors.Black;
        return valid;
    }
}

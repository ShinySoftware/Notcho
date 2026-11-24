namespace Notcho.Views;

/// <summary>
/// Marks a notch surface (a Border) as frosted glass, for the head to back with a blur of whatever is
/// behind the window. Set by <see cref="Ui.Paint"/>; only read on a head whose
/// <see cref="Platform.IPlatformUi.CanFrost"/> is true.
/// </summary>
public static class Frost
{
    public static readonly BindableProperty FrostedProperty = BindableProperty.CreateAttached("Frosted", typeof(bool), typeof(Frost), false);

    public static bool GetFrosted(BindableObject view) => (bool)view.GetValue(FrostedProperty);

    public static void SetFrosted(BindableObject view, bool value) => view.SetValue(FrostedProperty, value);
}

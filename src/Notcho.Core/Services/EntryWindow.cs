using Shiny.Maui.Controls.QuickEntry;

namespace Notcho.Services;

/// <summary>
/// Shiny's quick entry popup: one window, shared by everything typed outside the notch (which never takes
/// the keyboard) - a task, an event. Each puts its own content in as it opens it.
/// </summary>
static class EntryWindow
{
    /// <summary>One title for every use, since the window keeps the one it was made with.</summary>
    public const string Title = "Notcho Entry";

    public static void Show(IQuickEntryService quickEntry, double width, Func<View> content)
    {
        var options = quickEntry.Options;
        options.WindowTitle = Title;
        options.Width = width;
        options.ContentFactory = content;
        options.RecreateContentOnShow = true;
        quickEntry.Show();
    }
}

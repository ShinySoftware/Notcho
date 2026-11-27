using Shiny;
using Notcho.Platform;

namespace Notcho.Linux;

/// <summary>GTK's file dialog (which uses the desktop portal under Flatpak and Snap), limited to JSON files.</summary>
[Singleton]
sealed class LinuxFileDialogs : IFileDialogs
{
    public async Task<string?> SaveAsync(string title, string suggestedName)
    {
        var dialog = Dialog(title);
        dialog.SetInitialName(suggestedName);
        try
        {
            var file = await dialog.SaveAsync(null);
            return file?.GetPath();
        }
        catch (GLib.GException)
        {
            // Cancelled, or dismissed.
            return null;
        }
    }

    public async Task<string?> OpenAsync(string title)
    {
        var dialog = Dialog(title);
        try
        {
            var file = await dialog.OpenAsync(null);
            return file?.GetPath();
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    static Gtk.FileDialog Dialog(string title)
    {
        var json = Gtk.FileFilter.New();
        json.SetName("JSON files");
        json.AddPattern("*.json");
        var filters = Gio.ListStore.New(Gtk.FileFilter.GetGType());
        filters.Append(json);

        var dialog = Gtk.FileDialog.New();
        dialog.SetTitle(title);
        dialog.SetModal(true);
        dialog.SetFilters(filters);
        dialog.SetDefaultFilter(json);
        return dialog;
    }
}

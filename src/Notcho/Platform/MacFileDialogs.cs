using AppKit;
using Shiny;
using UniformTypeIdentifiers;
using Notcho.Platform;

namespace Notcho.MacOS;

/// <summary>NSSavePanel and NSOpenPanel, limited to JSON files.</summary>
[Singleton]
sealed class MacFileDialogs : IFileDialogs
{
    public Task<string?> SaveAsync(string title, string suggestedName)
    {
        var panel = NSSavePanel.SavePanel;
        panel.Title = title;
        panel.NameFieldStringValue = suggestedName;
        panel.AllowedContentTypes = [UTTypes.Json];
        panel.CanCreateDirectories = true;
        return Task.FromResult(Run(panel) ? panel.Url?.Path : null);
    }

    public Task<string?> OpenAsync(string title)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = title;
        panel.AllowedContentTypes = [UTTypes.Json];
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = false;
        return Task.FromResult(Run(panel) ? panel.Url?.Path : null);
    }

    /// <summary>
    /// Runs the panel modally. Notcho is an agent app with no Dock icon, so it's brought forward first, or
    /// the panel would open behind whatever app was in front.
    /// </summary>
    static bool Run(NSSavePanel panel)
    {
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
        return panel.RunModal() == (nint)(long)NSModalResponse.OK;
    }
}

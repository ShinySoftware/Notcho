using Shiny;
using Shiny.Maui.Controls.QuickEntry;
using Notcho.Platform;
using Notcho.Views;

namespace Notcho.Services;

/// <summary>
/// Typing a new task. The notch window never takes the keyboard, so tasks are typed into Shiny's quick
/// entry popup: a window of its own, over whatever app is in front. Return adds the task and clears the
/// box for the next one; Escape or clicking away closes it. The popup is shared (see <see cref="EntryWindow"/>).
/// </summary>
[Singleton]
public sealed class TaskEntry
{
    readonly IQuickEntryService quickEntry;
    readonly TasksService tasks;

    public TaskEntry(IQuickEntryService quickEntry, TasksService tasks, IPlatformShell shell)
    {
        this.quickEntry = quickEntry;
        this.tasks = tasks;
        // The popup is a floating window and the notch a status-bar one, so it opened under the panel.
        // Raised here for every use of the shared popup, not just a task's.
        quickEntry.Opened += (_, _) => shell.RaiseAboveNotch(EntryWindow.Title);

        var options = quickEntry.Options;
        options.DismissOnFocusLost = true;
        options.DismissOnEscape = true;
    }

    View Prompt()
    {
        var prompt = new PromptView
        {
            Placeholder = "Add a task, then Return. Esc when you're done.",
            ShowMicrophone = false,
            ClearOnSubmit = true,
            AccentColor = Ui.Blue,
            SurfaceColor = Ui.Surface,
            TextColor = Ui.Text,
            PlaceholderColor = Ui.Tertiary
        };
        prompt.Submitted += (_, e) => this.tasks.Add(e.Text);
        return prompt;
    }

    public bool IsOpen => this.quickEntry.IsOpen;

    /// <summary>The popup closed, whichever way.</summary>
    public event EventHandler? Closed
    {
        add => this.quickEntry.Closed += value;
        remove => this.quickEntry.Closed -= value;
    }

    public void Show() => EntryWindow.Show(this.quickEntry, 520, this.Prompt);
}

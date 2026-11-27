namespace Notcho.Platform;

/// <summary>The system's own Save and Open dialogs, for files the user puts where they choose.</summary>
public interface IFileDialogs
{
    /// <summary>Asks where to save a file. The chosen path, or null when cancelled.</summary>
    /// <param name="suggestedName">The name the dialog starts with, extension included.</param>
    Task<string?> SaveAsync(string title, string suggestedName);

    /// <summary>Asks for a JSON file to open. The chosen path, or null when cancelled.</summary>
    Task<string?> OpenAsync(string title);
}

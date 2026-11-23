namespace ShinyNotch.Platform;

/// <summary>
/// Holds off the computer's idle sleep, the way Amphetamine and Caffeine do. Only idle sleep: closing the
/// lid, choosing Sleep or a low battery still sleeps it. One per head.
/// </summary>
public interface IKeepAwake
{
    /// <summary>
    /// Keeps the computer from sleeping on its own; with <paramref name="display"/>, the screen stays on
    /// too. Replaces any hold already taken. Called on the main thread.
    /// </summary>
    void Hold(bool display);

    /// <summary>Lets the computer sleep again. Nothing to do when nothing is held.</summary>
    void Release();
}

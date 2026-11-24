using System.Diagnostics;

namespace Notcho;

/// <summary>
/// Renames a copy still called by the app's old name to Notcho.app. Copies from before the rename (1.3.0 and
/// earlier) update by installing the new app over their own bundle, keeping its file name: the updater swaps
/// bundles in place. So the first launch under the old name renames the bundle and opens it again from there,
/// before anything is on screen.
/// </summary>
static class LegacyBundleName
{
    const string BundleName = "Notcho.app";
    static readonly string[] OldNames = ["ShinyNotch.app", "Shiny Notch.app"];

    /// <summary>True when the app was renamed and has been relaunched from its new name, so this process should exit.</summary>
    public static bool RenameAndRelaunch()
    {
        try
        {
            // .../ShinyNotch.app/Contents/MacOS/Notcho
            var bundle = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath)));
            var folder = Path.GetDirectoryName(bundle);
            if (bundle == null || folder == null || !OldNames.Contains(Path.GetFileName(bundle)))
                return false;

            // A quarantined download runs from a read-only copy somewhere random; and an existing Notcho.app
            // (installed fresh beside the old one) is left alone.
            var renamed = Path.Combine(folder, BundleName);
            if (bundle.Contains("/AppTranslocation/", StringComparison.Ordinal) || Directory.Exists(renamed) || File.Exists(renamed))
                return false;

            // A running app's bundle can be renamed under it; the updater relies on the same.
            Directory.Move(bundle, renamed);

            // Opened once this process has gone, so the new launch isn't handed back to this one.
            using var relaunch = Process.Start(new ProcessStartInfo("/bin/sh")
            {
                ArgumentList = { "-c", "while kill -0 \"$0\" 2>/dev/null; do sleep 0.2; done; open \"$1\"", Environment.ProcessId.ToString(), renamed },
                UseShellExecute = false
            });
            if (relaunch != null)
                return true;

            // Couldn't relaunch: put the name back and carry on as before.
            Directory.Move(renamed, bundle);
            return false;
        }
        catch
        {
            // No permission to rename it, or anything else: the old name still works.
            return false;
        }
    }
}

using System.Runtime.InteropServices;
using Shiny;
using Notcho.Platform;

namespace Notcho.Windows;

/// <summary>
/// The common Save As and Open dialogs (comdlg32), limited to JSON files. Used over WinRT's pickers,
/// which need the owning window's handle wired in by hand and still misbehave in unpackaged apps.
/// </summary>
[Singleton]
sealed class WindowsFileDialogs : IFileDialogs
{
    const int MaxPath = 1024;
    const int OfnOverwritePrompt = 0x2;
    const int OfnPathMustExist = 0x800;
    const int OfnFileMustExist = 0x1000;
    const int OfnNoChangeDir = 0x8;

    public Task<string?> SaveAsync(string title, string suggestedName)
        => Task.FromResult(Show(title, suggestedName, save: true));

    public Task<string?> OpenAsync(string title)
        => Task.FromResult(Show(title, "", save: false));

    static string? Show(string title, string initialName, bool save)
    {
        var buffer = Marshal.AllocHGlobal(MaxPath * 2);
        try
        {
            var initial = (initialName + '\0').ToCharArray();
            Marshal.Copy(initial, 0, buffer, Math.Min(initial.Length, MaxPath));
            var dialog = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Owner = GetActiveWindow(),
                Filter = "JSON files (*.json)\0*.json\0All files (*.*)\0*.*\0\0",
                File = buffer,
                MaxFile = MaxPath,
                Title = title,
                DefaultExtension = "json",
                Flags = OfnNoChangeDir | OfnPathMustExist | (save ? OfnOverwritePrompt : OfnFileMustExist)
            };
            var chosen = save ? GetSaveFileName(ref dialog) : GetOpenFileName(ref dialog);
            return chosen ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OpenFileName
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        public string Filter;
        public IntPtr CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        public IntPtr File;
        public int MaxFile;
        public IntPtr FileTitle;
        public int MaxFileTitle;
        public string? InitialDirectory;
        public string Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        public string DefaultExtension;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
        public IntPtr Reserved;
        public int ReservedNumber;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW")]
    static extern bool GetSaveFileName(ref OpenFileName dialog);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW")]
    static extern bool GetOpenFileName(ref OpenFileName dialog);

    [DllImport("user32.dll")]
    static extern IntPtr GetActiveWindow();
}

using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PhotoGallery.App.Shell;

/// <summary>Opens a folder in Explorer, making it first if it is not there.</summary>
/// <remarks>
/// Created rather than reported as missing: the one place this is used is the
/// folder the user is being asked to download into, and "that folder does not
/// exist" is an unhelpful answer to somebody who has just been told to put files
/// in it.
///
/// <para>Not <see cref="OriginalInExplorer"/>, which selects a file inside its
/// folder and exists to answer "where is this picture really". This opens the
/// folder itself, and must not fail when it is empty.</para>
/// </remarks>
public static class FolderInExplorer
{
    public static void Open(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Window? owner = System.Windows.Application.Current?.MainWindow;

        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.ComponentModel.Win32Exception
                                      or InvalidOperationException)
        {
            AppDialog.Tell(
                owner,
                "That folder could not be opened",
                $"{folder}\n\n{ex.Message}",
                DialogTone.Caution);
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Opens Explorer on an original file, with it selected.
/// </summary>
/// <remarks>
/// The app only ever reads its own small copies of a picture, so this is the way
/// to the real thing - and every screen that names a file offers it.
/// </remarks>
public static class OriginalInExplorer
{
    public static void Show(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return;
        }

        Window? owner = System.Windows.Application.Current?.MainWindow;

        if (!File.Exists(fullPath))
        {
            // A drive not mounted, or a folder that has moved. Saying so beats
            // an Explorer window opened on nothing.
            AppDialog.Tell(
                owner,
                "File not available",
                $"That file is not there at the moment.\n\n{fullPath}\n\n"
                + "The folder it lives in may be disconnected.",
                DialogTone.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                      or InvalidOperationException)
        {
            AppDialog.Tell(
                owner,
                "Explorer could not be opened",
                $"{fullPath}\n\n{ex.Message}",
                DialogTone.Caution);
        }
    }
}

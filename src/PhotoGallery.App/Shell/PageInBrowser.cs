using System.Diagnostics;
using System.Windows;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Opens a web address in whichever browser Windows is set to use.
/// </summary>
/// <remarks>
/// A shell launch, not a request. This app makes no network call of its own and
/// this does not give it one: the address is handed to Windows, which opens it
/// in the browser the user already chose. That distinction is the reason About
/// links out to the releases page instead of checking for updates itself.
/// </remarks>
public static class PageInBrowser
{
    public static void Open(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        Window? owner = System.Windows.Application.Current?.MainWindow;

        try
        {
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                      or InvalidOperationException)
        {
            // A machine with no browser registered, most likely. Saying so beats
            // a click that appears to do nothing at all.
            AppDialog.Tell(
                owner,
                "That page could not be opened",
                $"{address}\n\n{ex.Message}",
                DialogTone.Caution);
        }
    }
}

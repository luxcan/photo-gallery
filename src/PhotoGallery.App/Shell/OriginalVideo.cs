using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Plays a video in whatever this machine already uses for one.
/// </summary>
/// <remarks>
/// Playing inside the app is out of scope for [08] and is not what this does.
/// The app holds one still taken out of the film, so watching it means handing
/// the file to Windows - which is the same bargain the whole feature rests on:
/// no decoder bundled, no codec knowledge here.
///
/// <para>A sibling of <see cref="OriginalInExplorer"/> rather than a method on
/// it, because the intent differs - one reveals a file, the other opens it - and
/// folding them together would leave a type named for Explorer doing neither
/// thing plainly. What they share is the guard, and it is six lines.</para>
/// </remarks>
public static class OriginalVideo
{
    public static void Play(string? fullPath)
    {
        Window? owner = System.Windows.Application.Current?.MainWindow;

        if (string.IsNullOrEmpty(fullPath) || !Exists(fullPath))
        {
            // The case this exists for. The poster is a copy kept on this
            // machine, so a video whose folder is disconnected still looks
            // perfectly present right up until somebody asks to watch it - and
            // a button that did nothing would read as the app being broken
            // rather than the folder being away.
            AppDialog.Tell(
                owner,
                "Video not available",
                string.IsNullOrEmpty(fullPath)
                    ? "Photo Gallery does not know where this video is kept."
                    : $"This video is not there at the moment.\n\n{fullPath}\n\n"
                      + "The folder it lives in may be disconnected. The picture you "
                      + "can see is a copy Photo Gallery keeps; the film itself is "
                      + "still wherever you put it.",
                DialogTone.Information);
            return;
        }

        try
        {
            // UseShellExecute, so Windows picks whatever plays this kind of file
            // rather than this app having an opinion about players.
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                      or InvalidOperationException
                                      or FileNotFoundException)
        {
            // Reached when nothing on this machine is registered to play it, or
            // the file went between the check above and here.
            AppDialog.Tell(
                owner,
                "Video not available",
                $"Windows could not play this video.\n\n{fullPath}\n\n{ex.Message}",
                DialogTone.Caution);
        }
    }

    private static bool Exists(string fullPath)
    {
        try
        {
            return File.Exists(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A share that is away throws here rather than answering false.
            return false;
        }
    }
}

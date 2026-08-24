using System.Runtime.InteropServices;
using System.Windows;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Puts a link on the clipboard, and says whether it got there.
/// </summary>
/// <remarks>
/// Returns a result rather than telling the user itself, unlike the other shell
/// helpers here. A failed copy is worth a line beside the button that was
/// pressed, not a modal - and the caller is the only thing that knows where
/// that line goes.
///
/// <para><c>SetDataObject</c> rather than <c>SetText</c>, and tried once. The
/// clipboard is a single system-wide lock that any clipboard manager or remote
/// desktop session can be holding, and this call already retries inside itself -
/// ten attempts a hundred milliseconds apart, about a second in all - before it
/// gives up. Anything still refusing after that is a real refusal and worth
/// saying so, not a race worth a second blind attempt at another second's
/// cost.</para>
/// </remarks>
public static class LinkOnClipboard
{
    public static bool Copy(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return false;
        }

        try
        {
            Clipboard.SetDataObject(link, copy: true);
            return true;
        }
        catch (ExternalException)
        {
            // CLIPBRD_E_CANT_OPEN, and the COMException it arrives as - both are
            // this exception, so the base type is the whole net.
            return false;
        }
    }
}

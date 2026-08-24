using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PhotoGallery.App.Theme;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Paints the window's own title bar to match the theme.
/// </summary>
/// <remarks>
/// The title bar is the one piece of this app Windows draws, and left alone it
/// follows the operating system rather than the app: choose the dark theme on a
/// machine set to light and a white caption sits on top of a near-black window.
///
/// <para>Asking the desktop manager to recolour its own caption rather than
/// replacing it. A custom title bar would match the design more exactly and
/// costs far more than it looks - snap layouts, the drag region, double-click to
/// maximise, the caption buttons' hover semantics and their right-to-left
/// mirroring are all behaviour Windows provides and a hand-drawn bar has to
/// reimplement. Three attributes get almost all of the appearance and none of
/// that risk.</para>
///
/// <para>Everything here degrades quietly. The attributes arrived in different
/// Windows versions, an older build simply refuses the ones it does not know,
/// and the result is the title bar that would have been drawn anyway.</para>
/// </remarks>
public static class TitleBarPainter
{
    /// <summary>Dark caption buttons and title text. Windows 10 20H1 and later.</summary>
    private const int UseImmersiveDarkMode = 20;

    /// <summary>The caption's own colour. Windows 11 build 22000 and later.</summary>
    private const int CaptionColour = 35;

    /// <summary>The title text's colour, same versions as the caption's.</summary>
    private const int TextColour = 36;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode)]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int size);

    /// <summary>
    /// Repaints one window's title bar for the palette now loaded.
    /// </summary>
    /// <remarks>
    /// Safe to call before the window is shown; it does nothing until there is a
    /// handle to talk about, and the caller repeats it once there is.
    /// </remarks>
    public static void Paint(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int dark = ThemeManager.IsDark ? 1 : 0;
        Set(handle, UseImmersiveDarkMode, dark);

        // The same brushes the rest of the shell uses, so the caption cannot
        // drift from the window beneath it.
        if (Colour("TitleBar.Background") is Color background)
        {
            Set(handle, CaptionColour, ToColorRef(background));
        }

        if (Colour("TitleBar.Foreground") is Color foreground)
        {
            Set(handle, TextColour, ToColorRef(foreground));
        }
    }

    /// <summary>
    /// Keeps a window's title bar in step for as long as it lives.
    /// </summary>
    /// <remarks>
    /// Three moments matter and all three are needed: when the handle first
    /// exists, because there is nothing to paint before that; when the theme
    /// changes, because that is the whole point; and once more after the window
    /// is shown, since Windows draws the caption before the first paint on some
    /// builds and keeps whatever it drew.
    /// </remarks>
    public static void Follow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        void Repaint(Domain.Library.ThemePreference _) => Paint(window);

        window.SourceInitialized += (_, _) => Paint(window);
        window.ContentRendered += (_, _) => Paint(window);

        ThemeManager.Changed += Repaint;
        window.Closed += (_, _) => ThemeManager.Changed -= Repaint;
    }

    private static void Set(IntPtr handle, int attribute, int value)
    {
        int local = value;

        // The return value is deliberately ignored: an attribute this build of
        // Windows does not know is refused, and being refused is the answer -
        // the caption stays as Windows would have drawn it.
        _ = DwmSetWindowAttribute(handle, attribute, ref local, sizeof(int));
    }

    /// <summary>The palette's colour for a key, or null when it holds no brush.</summary>
    private static Color? Colour(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) is SolidColorBrush brush
            ? brush.Color
            : null;

    /// <summary>
    /// Windows wants 0x00BBGGRR, which is the reverse of how a colour is written
    /// everywhere else in this app.
    /// </summary>
    private static int ToColorRef(Color colour) =>
        colour.R | (colour.G << 8) | (colour.B << 16);
}

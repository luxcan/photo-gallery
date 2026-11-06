using System.Windows;
using System.Windows.Controls;

namespace PhotoGallery.App.Theme;

/// <summary>
/// How quickly a tooltip appears, the same for every control in the app.
/// </summary>
/// <remarks>
/// Set as the default for every element rather than on each control, so a
/// tooltip added later picks it up without anybody remembering to ask. Left
/// alone, WPF waits for the Windows hover time - 400 ms here - which felt slow
/// when hovering along the photo wall.
/// </remarks>
public static class ToolTipTiming
{
    /// <summary>Milliseconds between the pointer arriving and the tooltip showing.</summary>
    public const int InitialShowDelay = 250;

    private static bool s_applied;

    /// <summary>
    /// Makes <see cref="InitialShowDelay"/> the default for every element.
    /// Safe to call more than once; only the first call does anything, because
    /// WPF allows a type's metadata to be overridden only once.
    /// </summary>
    public static void Apply()
    {
        if (s_applied)
        {
            return;
        }

        // Hyperlinks and runs are content elements, not framework elements, and
        // carry tooltips too - so both roots get the new default.
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(InitialShowDelay));
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(FrameworkContentElement), new FrameworkPropertyMetadata(InitialShowDelay));

        s_applied = true;
    }
}

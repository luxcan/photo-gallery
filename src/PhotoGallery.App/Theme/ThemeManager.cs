using System.Windows;
using Microsoft.Win32;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.App.Theme;

/// <summary>
/// Swaps the palette dictionary at runtime.
/// </summary>
/// <remarks>
/// Every control binds colours with <c>DynamicResource</c>, so replacing the
/// dictionary re-renders the whole shell without rebuilding any view. Defaults
/// to following Windows, as VS Code does, until the user picks a side - at which
/// point the choice is stored with the library and restored next time.
/// </remarks>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri s_darkSource = new("Theme/Dark.xaml", UriKind.Relative);
    private static readonly Uri s_lightSource = new("Theme/Light.xaml", UriKind.Relative);

    public static ThemePreference Current { get; private set; } = ThemePreference.System;

    /// <summary>The palette actually on screen once <see cref="ThemePreference.System"/> is resolved.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Raised when the palette changes, so the choice can be saved.</summary>
    public static event Action<ThemePreference>? Changed;

    public static void Apply(ThemePreference theme)
    {
        Current = theme;
        IsDark = theme switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => IsWindowsInDarkMode(),
        };

        var palette = new ResourceDictionary
        {
            Source = IsDark ? s_darkSource : s_lightSource,
        };

        // The palette is always first; styles merged after it resolve against
        // whichever palette is currently loaded. (System.Windows.Application is
        // spelled out because PhotoGallery.Application shadows it.)
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Add(palette);
        }
        else
        {
            merged[0] = palette;
        }

        Changed?.Invoke(theme);
    }

    /// <summary>Cycles Dark to Light and back, pinning the choice away from System.</summary>
    public static void Toggle() =>
        Apply(IsDark ? ThemePreference.Light : ThemePreference.Dark);

    private static bool IsWindowsInDarkMode()
    {
        // AppsUseLightTheme is 0 for dark. A missing value means an older
        // Windows that only had the light theme.
        object? value = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
        return value is int i && i == 0;
    }
}

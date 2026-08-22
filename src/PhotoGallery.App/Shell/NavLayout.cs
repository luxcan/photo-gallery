namespace PhotoGallery.App.Shell;

/// <summary>
/// The side nav's two widths, and the window width it folds itself at.
/// </summary>
/// <remarks>
/// Here rather than as literals in the XAML so the numbers the tests assert on
/// and the numbers drawn on screen are the same three fields, in the shape
/// <see cref="Gallery.GalleryLayout"/> already uses for the grid. No WPF type is
/// named, so the rule can be exercised without a dispatcher.
/// </remarks>
public static class NavLayout
{
    /// <summary>Open: room for a name and a count beside every icon.</summary>
    public const double ExpandedWidth = 196d;

    /// <summary>
    /// Folded. 12 of padding, a 2 DIP rail that is overlaid rather than laid out,
    /// 7, the 14 DIP icon, 7, and 12 again - which puts the icon's centre on 26,
    /// exactly half the folded width.
    /// </summary>
    public const double CollapsedWidth = 52d;

    /// <summary>
    /// Below this the nav folds itself, because at that size the content is
    /// worth more than the names are. The user can always fold it back open.
    /// </summary>
    public const double NarrowWindowWidth = 1100d;

    public static bool IsNarrow(double windowWidth) => windowWidth < NarrowWindowWidth;
}

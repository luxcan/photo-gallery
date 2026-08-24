namespace PhotoGallery.Domain.Library;

/// <summary>
/// Library-wide settings, stored as a single row in its own index. Where the
/// photos live is not a setting - that is the <see cref="PhotoSource"/> list.
/// </summary>
public sealed class LibrarySettings
{
    /// <summary>Always 1 - there is exactly one library per working folder.</summary>
    public int Id { get; set; } = 1;

    public DateTime? LastScanUtc { get; set; }

    /// <summary>
    /// The palette last chosen. Defaults to following Windows until the user
    /// picks a side.
    /// </summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>
    /// The edge of one gallery cell, in device-independent pixels. Belongs to the
    /// library rather than the machine: how big a thumbnail wants to be depends on
    /// the pictures, and it travels with them.
    /// </summary>
    /// <remarks>
    /// Stored rather than derived because it is a preference, and clamped on the
    /// way in by the gallery: a value outside the stops - from a hand-edited
    /// database, or a stop that a later release removes - must not be able to
    /// produce a cell the tiles cannot fill.
    /// </remarks>
    public double GalleryCellSize { get; set; } = 200d;

    /// <summary>
    /// Which end of the library the grid starts at. Belongs to the library for
    /// the same reason as the zoom: it describes how you look at these pictures.
    /// </summary>
    public GallerySortOrder GallerySortOrder { get; set; } = GallerySortOrder.NewestFirst;

    /// <summary>
    /// Whether the side nav is folded down to its icons.
    /// </summary>
    /// <remarks>
    /// Belongs to the library for the same reason as the zoom and the palette:
    /// it describes how you look at these pictures. Only a deliberate fold is
    /// stored - a narrow window folds the nav on its own, and remembering that
    /// would let one session on a small screen decide how the next one opens.
    /// </remarks>
    public bool NavigationCollapsed { get; set; }
}

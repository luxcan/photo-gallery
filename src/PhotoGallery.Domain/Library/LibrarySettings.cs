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

    /// <summary>
    /// What this machine calls itself when it publishes what it has decided.
    /// Minted once, and never reused.
    /// </summary>
    public Guid MachineId { get; set; }

    /// <summary>
    /// What the other machines show for it, defaulting to the computer's own
    /// name. Editable, and carries no meaning: nothing is decided from it.
    /// </summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// The folder every machine in the house writes its answers into, or null
    /// while sharing has not been set up.
    /// </summary>
    /// <remarks>
    /// Nominated by the user, once. It must not sit inside a photo source and no
    /// photo source may sit inside it, and the check runs both ways: the pooled
    /// renditions are ordinary <c>.jpg</c> files in a folder tree, so a scan would
    /// index them as photographs and the library would grow a second copy of
    /// itself every time anybody pressed Refresh.
    ///
    /// <para>This one is machine-local and travels least of all: it is the route
    /// <em>this</em> computer takes to the folder, and the same folder is a
    /// mapped drive letter on the next one. Only <see cref="MachineId"/> and
    /// <see cref="MachineName"/> leave here, and they go as the signature on a
    /// decision set rather than as settings. The theme, the cell size, the sort
    /// order and the nav state describe how one person looks at their pictures on
    /// one screen and belong on no other machine - worth saying where the three
    /// sharing fields sit, because sending the whole row is the obvious wrong
    /// thing to do and is one line away.</para>
    /// </remarks>
    public string? SharedFolder { get; set; }
}

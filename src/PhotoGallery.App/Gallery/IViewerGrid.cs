namespace PhotoGallery.App.Gallery;

/// <summary>
/// Another screen's grid that the photograph viewer can be opened from, and
/// that can be asked about one photograph once the viewer has moved off it.
/// </summary>
/// <remarks>
/// The viewer already kept the library's own wall honest: a photograph filed
/// or named while it was open was checked against the wall's question once the
/// viewer left it, and taken off if it no longer answered. A photograph opened
/// from an album or from somebody's page was never checked against anything,
/// so moving it to another album left it on the first album's page until the
/// page was closed and opened again - the very bug that was reported.
///
/// <para>Each screen answers its own question rather than the viewer copying
/// its rules, for the same reason the wall asks its reader: a second copy of an
/// album's or a person's rule inside the viewer would drift from the screen's
/// own the first time either changed.</para>
/// </remarks>
public interface IViewerGrid
{
    /// <summary>The tiles the viewer steps through.</summary>
    TileWindow Window { get; }

    /// <summary>
    /// Which of these photographs the screen's own question, asked now, still
    /// has in the answer.
    /// </summary>
    /// <remarks>
    /// Asked of what the screen is showing now rather than what it showed when
    /// the photograph was opened, because the grids are reused: an album's page
    /// holds whichever album is open, and a person's page whoever is chosen.
    ///
    /// <para>Several at once, because a photograph is settled together with
    /// every copy of it on the grid, and the proposals' question is a search of
    /// the whole library: asked once per copy, it was one search after another
    /// for a single press of an arrow.</para>
    /// </remarks>
    Task<IReadOnlySet<int>> StillShowingAsync(IReadOnlyList<int> assetIds);

    /// <summary>
    /// Puts right whatever the screen says about its grid, after this tile has
    /// been taken off <see cref="Window"/>.
    /// </summary>
    /// <remarks>
    /// Taking a tile off the window leaves headings, counts and any list the
    /// screen keeps beside the window exactly as they were. A count left one too
    /// high reads as the move not having happened, and a list left holding the
    /// tile puts it straight back the next time the screen regroups.
    /// </remarks>
    void Dropped(GalleryTile tile);
}

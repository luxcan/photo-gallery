using System.Windows.Media;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Albums;

/// <summary>One collection as a row in the band.</summary>
/// <remarks>
/// Deliberately not an album card. A shelf and an album open into different
/// worlds - one into more cards, one into photographs - and drawing both as a
/// 180px cover with a name under it told the reader nothing about which was
/// which. This is a short wide row carrying a small mosaic of what is on the
/// shelf, so the two are told apart by their shape rather than by a badge.
/// </remarks>
public sealed record CollectionItem(CollectionSummary Summary, IReadOnlyList<ImageSource?> Covers)
{
    /// <summary>How many tiles the mosaic has, filled or not.</summary>
    /// <remarks>
    /// Always four, so the grid is square whatever the shelf holds and a cover
    /// arriving does not change the row's size under the pointer.
    /// </remarks>
    public const int MosaicTiles = 4;

    /// <summary>An unfilled mosaic, for a row that has just been read.</summary>
    public static IReadOnlyList<ImageSource?> NoCovers { get; } =
        new ImageSource?[MosaicTiles];

    public int Id => Summary.Id;

    public string Name => Summary.Name;

    /// <summary>
    /// Whether there is anything on the shelf, which decides what is drawn in
    /// place of the mosaic.
    /// </summary>
    /// <remarks>
    /// An empty shelf gets an outline and a plus rather than four blank tiles.
    /// Blank tiles read as pictures that failed to load; an outline reads as
    /// somewhere to put something, which is what it is.
    /// </remarks>
    public bool HasAlbums => Summary.AlbumCount > 0;

    /// <summary>How many albums, and how many photographs between them.</summary>
    /// <remarks>
    /// No dates. An album covers an occasion and its caption says when; a
    /// collection is a theme and may be a decade wide, so a span here would be
    /// a fact nobody came for.
    /// </remarks>
    public string Caption => Summary.AlbumCount == 0
        ? "Empty - add albums"
        : $"{Albums} · {Photos}";

    private string Albums =>
        Summary.AlbumCount == 1 ? "1 album" : $"{Summary.AlbumCount:N0} albums";

    private string Photos =>
        Summary.PhotoCount == 1 ? "1 photo" : $"{Summary.PhotoCount:N0} photos";
}

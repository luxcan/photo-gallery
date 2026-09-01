using System.Windows.Media;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Albums;

/// <summary>One collection as a card in the band.</summary>
/// <remarks>
/// Display only, and deliberately the same shape as an album's card: the same
/// cover, the same name, the same one-line caption underneath. What tells the
/// two apart is where they are - a band across the top under its own heading -
/// rather than a badge on every card, which is the reason only a trip earns one
/// on the wall below.
/// </remarks>
public sealed record CollectionItem(CollectionSummary Summary, ImageSource? Cover)
{
    public int Id => Summary.Id;

    public string Name => Summary.Name;

    /// <summary>
    /// How many albums, and how many photographs between them.
    /// </summary>
    /// <remarks>
    /// No dates. An album covers an occasion and its caption says when; a
    /// collection is a theme and may be a decade wide, so a span here would be
    /// a fact nobody came for.
    /// </remarks>
    public string Caption => Summary.AlbumCount == 0
        ? "No albums yet"
        : $"{Albums}, {Photos}";

    private string Albums =>
        Summary.AlbumCount == 1 ? "1 album" : $"{Summary.AlbumCount:N0} albums";

    private string Photos =>
        Summary.PhotoCount == 1 ? "1 photo" : $"{Summary.PhotoCount:N0} photos";
}

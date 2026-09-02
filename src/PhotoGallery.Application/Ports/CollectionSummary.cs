namespace PhotoGallery.Application.Ports;

/// <summary>One collection as the band reads it: its name, and how much is on it.</summary>
/// <param name="CoverThumbnailNames">
/// Up to four renditions to draw for it, most recently taken album first, and
/// empty while it holds no album with a picture.
/// </param>
/// <remarks>
/// Four rather than one, because the band draws a shelf as a small mosaic of
/// what is on it rather than as a single cover. One cover made a collection card
/// identical to an album card, which is the one thing the two levels must not
/// look like, and it left an empty shelf as a hole the size of a photograph.
///
/// <para>No span. An album covers an occasion and says when; a collection is a
/// theme and may be a decade wide, so a date range under its name would be noise
/// rather than an answer.</para>
/// </remarks>
public sealed record CollectionSummary(
    int Id,
    string Name,
    int AlbumCount,
    int PhotoCount,
    IReadOnlyList<string> CoverThumbnailNames);

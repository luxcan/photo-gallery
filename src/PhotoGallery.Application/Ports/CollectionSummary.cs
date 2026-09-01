namespace PhotoGallery.Application.Ports;

/// <summary>One collection as the band reads it: its name, and how much is on it.</summary>
/// <param name="CoverThumbnailName">
/// The rendition to draw for it - the cover of its most recently taken album -
/// or null while it holds no album with a picture. A card with no picture still
/// shows: an empty shelf is one somebody made and has not filled yet, and
/// hiding it would lose the name they typed.
/// </param>
/// <remarks>
/// No span. An album covers an occasion and says when; a collection is a theme
/// and may be a decade wide, so a date range under its name would be noise
/// rather than an answer.
/// </remarks>
public sealed record CollectionSummary(
    int Id,
    string Name,
    int AlbumCount,
    int PhotoCount,
    string? CoverThumbnailName);

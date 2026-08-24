namespace PhotoGallery.Application.Ports;

/// <summary>
/// Everything about one picture that a detail panel shows.
/// </summary>
/// <remarks>
/// Read for the one picture being looked at rather than carried on every row.
/// The gallery holds eleven thousand items at once and uses none of this to draw
/// a grid of fixed squares, so putting it on <see cref="GalleryItem"/> would be
/// four more fields per row for a panel that shows one at a time.
/// </remarks>
/// <param name="ContentHash">
/// The SHA-256 of the whole file. Two pictures showing the same one are the same
/// file, which is what makes it worth showing beside the rest.
/// </param>
/// <param name="PlaceName">
/// Where it was taken, or null when that is not known - which is most
/// photographs, because most cameras have no receiver. The panel leaves the row
/// out entirely rather than showing "Unknown": a blank beside a label is a
/// question, and this one has no answer to give.
/// </param>
public sealed record PhotoFacts(
    int AssetId,
    string FileName,
    string FolderPath,
    string FullPath,
    long Length,
    int? Width,
    int? Height,
    DateTime? TakenUtc,
    DateTime ModifiedUtc,
    string? ContentHash,
    string? PlaceName = null,
    DateTime CreatedUtc = default);

using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// Two renditions produced from a single decode of one original.
/// </summary>
/// <remarks>
/// Reading and decoding the original is what costs; emitting a second size from
/// the pixels already in memory is nearly free. So the pass produces both at
/// once rather than forcing a second pass over the originals later.
///
/// <para><see cref="Tile"/> is what the gallery grid loads - thousands of them,
/// so it is kept small. <see cref="Preview"/> is for viewing one photo and, in
/// time, for face detection, which needs enough resolution to find a small face
/// in a group shot.</para>
///
/// <para>The date and the hash ride along for the same reason: both are
/// obtainable from a decode that has already happened, and neither is worth a
/// second pass over 25 GB of originals to collect later.</para>
/// </remarks>
/// <param name="TakenUtc">
/// EXIF <c>DateTimeOriginal</c>, or null where the file carries none - 11% of
/// this library. Note it is wall-clock time as the camera recorded it: EXIF
/// stores no time zone, so it cannot honestly be converted.
/// </param>
/// <param name="ContentHash">
/// Hex digest of the original's bytes, which is what the stored renditions are
/// named after. It is the only identity that survives a source being detached
/// and re-added, a share changing address, and the database renumbering its
/// rows - and it costs nothing here, because the bytes are already in hand.
/// </param>
/// <param name="Latitude">
/// Signed decimal degrees from the GPS block of the same metadata, or null where
/// the file carries none - 61% of this library. Beside the capture date because
/// it comes from the same read for the same reason: it is free here and costs an
/// hour over the share to collect later.
/// </param>
public sealed record GeneratedThumbnail(
    byte[] Tile,
    byte[] Preview,
    int SourceWidth,
    int SourceHeight,
    DateTime? TakenUtc,
    PerceptualHash PerceptualHash,
    string ContentHash,
    double? Latitude = null,
    double? Longitude = null)
{
    public int TotalBytes => Tile.Length + Preview.Length;
}

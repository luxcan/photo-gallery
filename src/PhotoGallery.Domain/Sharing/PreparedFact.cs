using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Everything one machine's decode of a photograph produced, offered to the
/// others.
/// </summary>
/// <remarks>
/// <strong>The whole result of the preparing pass, not a file list.</strong> The
/// decode that makes a thumbnail is also the only moment the app learns the
/// capture date, the dimensions, the coordinates and the perceptual hash. A
/// machine that copied the pictures but not those facts would get a library with
/// no timeline, no places and no albums: on this library that is 9,544 capture
/// dates and 1,709 sets of coordinates missing, and nothing to cluster
/// occasions from.
///
/// <para>Measured here: 15,823 rows, 4.20 MB of JSON, 1018 KB gzipped. One
/// megabyte to skip an hour of reading 24.8 GB.</para>
///
/// <para><see cref="Status"/> rides along so that the twelve files which will
/// never decode are not read again on four more machines - the same reason it
/// exists locally.</para>
/// </remarks>
/// <param name="Length">
/// With <paramref name="ModifiedUtc"/>, the check that decides whether the
/// pooled picture is a picture of <em>these</em> bytes. This is the one place
/// the change detector is load-bearing rather than advisory: getting it wrong
/// shows the wrong photograph, silently, and the person looking at it has no way
/// to tell.
/// </param>
/// <param name="ThumbnailName">
/// What the two renditions are called in the pool. Null for a file that never
/// decoded, which is a fact worth carrying and not a picture to fetch.
/// </param>
public sealed record PreparedFact(
    AssetKey Photo,
    long Length,
    DateTime ModifiedUtc,
    string? ContentHash,
    string? ThumbnailName,
    int Width,
    int Height,
    DateTime? TakenUtc,
    double? Latitude,
    double? Longitude,
    string? PerceptualHash,
    TimeSpan? Duration,
    AssetStatus Status)
{
    /// <summary>Whether this fact brings a picture with it, or only what was learnt.</summary>
    public bool HasPicture => !string.IsNullOrEmpty(ThumbnailName);

    /// <summary>
    /// Whether this describes the same bytes as a file here does.
    /// </summary>
    /// <remarks>
    /// Byte for byte and to the second. The path says which photograph, but a
    /// pooled rendition is of particular bytes: a copy of the same picture that
    /// was re-saved, cropped or re-encoded is a different file wearing the same
    /// name, and taking its rendition would put the wrong image on the screen
    /// with nothing to say so.
    /// </remarks>
    public bool Describes(long length, DateTime modifiedUtc) =>
        Length == length && ModifiedUtc == modifiedUtc;
}

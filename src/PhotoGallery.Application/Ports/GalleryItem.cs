using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Application.Ports;

/// <summary>One picture as the gallery shows it.</summary>
/// <remarks>
/// Width and height are deliberately absent. They are null for anything not yet
/// prepared, and the grid draws fixed square cells, so an aspect ratio would be
/// data the view cannot use and cannot trust.
/// </remarks>
/// <param name="SortedOn">
/// The date the row was ordered by - the capture date where the photo carries
/// one, otherwise the file's own. Carried so the view can say which it is
/// rather than presenting a file date as if it were when the shutter fired.
/// </param>
/// <param name="FullPath">
/// Where the file actually is, so the picture can be opened outside this app.
/// Empty when the row's source has gone, which is the only case where the root
/// is not known.
/// </param>
/// <param name="Duration">
/// How long a video runs, or null.
/// </param>
/// <remarks>
/// Null for every photograph, and for a good many videos too: the extractor that
/// asks the Windows shell for a poster is not told the length, because the shell
/// hands back a picture and nothing else. A clip whose length is not known shows
/// the badge without it rather than a made-up figure.
/// </remarks>
public sealed record GalleryItem(
    int Id,
    string RelativePath,
    string FileName,
    string FolderPath,
    string FullPath,
    string? ThumbnailName,
    DateTime? TakenUtc,
    DateTime SortedOn,
    int Rotation,
    AssetKind Kind,
    TimeSpan? Duration = null)
{
    /// <summary>
    /// Whether this picture is only upright inside this app.
    /// </summary>
    /// <remarks>
    /// A turn is recorded here only while the file itself cannot be told which
    /// way up it goes. Once it can, the tag is written and this goes back to
    /// none - so anything left is exactly the set of pictures that still look
    /// wrong in Explorer, and the viewer says so rather than letting the user
    /// discover it later.
    /// </remarks>
    public bool IsTurnedInAppOnly => Rotation != 0;
}

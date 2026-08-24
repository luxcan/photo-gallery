using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// Where to find one face on screen, and enough about the picture it came out of
/// to judge it.
/// </summary>
/// <remarks>
/// The bounds are in the pixels of the cached preview, which is the picture the
/// detector actually looked at - so a crop is cut from the rendition already on
/// disk and no new file is ever written for a face.
///
/// <para>The date and the path are carried because a crop on its own is often
/// not enough to answer "is this them?". A child at three and at seven are
/// different faces, the folder a picture was filed under is frequently the only
/// thing that says which occasion it was, and the file name is what lets the
/// same picture be found outside this app.</para>
/// </remarks>
public sealed record FaceThumbnail(
    int FaceId,
    int AssetId,
    string ThumbnailName,
    FaceBounds Bounds,
    DateTime TakenUtc,
    string RelativePath,
    string FullPath);

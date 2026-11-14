using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.Duplicates;

/// <summary>One known person's face in one copy, as the ladder reads it.</summary>
/// <param name="FaceId">The face row.</param>
/// <param name="PersonId">Who it is: confirmed, or matched by the app, to a named person.</param>
/// <param name="PersonName">The name the card says when this face's eyes cost the copy.</param>
/// <param name="Bounds">Where it is, in the cached preview's pixels, for the closed marker.</param>
/// <param name="Eyes">
/// What the eye step read, or null when the eyes were never read (the eye model
/// is not installed, or the face has not been measured yet).
/// </param>
/// <param name="Sharpness">
/// The variance of the Laplacian over the face's box with the preview at the
/// common size, or null when it was not measured.
/// </param>
/// <remarks>
/// Only known faces ever reach the ladder. A stranger, a face nobody has named,
/// a rejected or cleared match and an ignored face are left out where the rows
/// are read, so they cannot decide anything here.
/// </remarks>
public sealed record CopyFace(
    int FaceId,
    int PersonId,
    string PersonName,
    FaceBounds Bounds,
    EyeState? Eyes,
    double? Sharpness);

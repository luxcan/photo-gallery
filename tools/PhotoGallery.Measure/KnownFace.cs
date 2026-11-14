using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Measure;

/// <summary>
/// A face confirmed as, or matched by the app to, a named person who has not
/// been deleted, on a face nobody marked Ignore - the only faces PRP 14 counts.
/// </summary>
internal sealed record KnownFace(
    int FaceId,
    int AssetId,
    FaceBounds Bounds,
    float DetectScore,
    int PersonId,
    string PersonName,
    string Source);

using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// One face found in one cached preview, ready to be recorded.
/// </summary>
/// <remarks>
/// The bounds are in the preview's own pixels rather than the original's,
/// because the preview is what was looked at and is what will be drawn on.
/// </remarks>
public sealed record ScannedFace(FaceBounds Bounds, float DetectScore, FaceEmbedding Embedding);

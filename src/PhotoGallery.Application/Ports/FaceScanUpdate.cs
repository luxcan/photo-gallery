namespace PhotoGallery.Application.Ports;

/// <summary>
/// What one preview yielded, and which photos it speaks for.
/// </summary>
/// <remarks>
/// More than one asset can share a rendition - two byte-identical photos are the
/// same picture - so the faces found in it belong to all of them. The scan
/// happens once and every row gets its own copy.
/// </remarks>
public sealed record FaceScanUpdate(
    IReadOnlyList<int> AssetIds,
    IReadOnlyList<ScannedFace> Faces,
    DateTime DetectedUtc);

namespace PhotoGallery.Domain.Duplicates;

/// <summary>One copy's measurements, as the keeper ladder reads them.</summary>
/// <param name="AssetId">The copy.</param>
/// <param name="Pixels">Width times height, or zero when the size was never recorded.</param>
/// <param name="TakenUtc">The capture time read from the file, if it still has one.</param>
/// <param name="HasCoordinates">Whether the file still carries where it was taken.</param>
/// <param name="Sharpness">
/// The variance of the Laplacian over the most detailed quarter of the preview
/// at the common size, or null when it was not measured or the preview is
/// smaller than the common size.
/// </param>
/// <param name="ClippedHigh">The share of the preview blown white, or null when unmeasured.</param>
/// <param name="ClippedLow">The share of the preview crushed black, or null when unmeasured.</param>
/// <param name="KnownFaces">The known people's faces in it. Never a stranger's.</param>
/// <param name="PreviewWidth">
/// The measured preview's width, the space the face boxes are in; zero when it
/// was never measured. Not read by the ladder - it is what lets the screen put
/// a closed marker over the right face.
/// </param>
/// <param name="PreviewHeight">The measured preview's height, or zero.</param>
public sealed record CopyQuality(
    int AssetId,
    long Pixels,
    DateTime? TakenUtc,
    bool HasCoordinates,
    double? Sharpness,
    double? ClippedHigh,
    double? ClippedLow,
    IReadOnlyList<CopyFace> KnownFaces,
    int PreviewWidth = 0,
    int PreviewHeight = 0)
{
    /// <summary>
    /// Whether the file still carries the camera's details, which messaging apps
    /// and some editors strip when they save a photograph again.
    /// </summary>
    public bool CarriesCameraDetails => TakenUtc is not null || HasCoordinates;
}

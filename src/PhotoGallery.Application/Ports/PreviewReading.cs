namespace PhotoGallery.Application.Ports;

/// <summary>What one cached preview measured, with no model.</summary>
/// <param name="Width">The preview's width in pixels: the space the face boxes are in.</param>
/// <param name="Height">The preview's height in pixels.</param>
/// <param name="Sharpness">
/// The variance of the Laplacian over the most detailed quarter, measured with
/// the preview at the common size; null when the preview is smaller than that
/// size and so cannot be compared at it.
/// </param>
/// <param name="ClippedHigh">The share of pixels blown white.</param>
/// <param name="ClippedLow">The share of pixels crushed black.</param>
/// <param name="FaceSharpness">
/// One reading per face asked about, in the order given, measured the same way
/// over that face's box; null where the preview is too small or the box holds
/// nothing to measure.
/// </param>
public sealed record PreviewReading(
    int Width,
    int Height,
    double? Sharpness,
    double ClippedHigh,
    double ClippedLow,
    IReadOnlyList<double?> FaceSharpness);

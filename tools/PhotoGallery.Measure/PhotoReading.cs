using PhotoGallery.Infrastructure.Faces;
using PhotoGallery.Infrastructure.Imaging;

namespace PhotoGallery.Measure;

/// <summary>
/// Everything measured on one photograph's preview. Null measurements mean the
/// preview could not be read.
/// </summary>
/// <param name="Faces">Each known face on it with its eye measurement, in face order.</param>
internal sealed record PhotoReading(
    int AssetId,
    string? PreviewPath,
    int? PreviewWidth,
    int? PreviewHeight,
    double? FaceSharpness,
    double? SceneSharpness,
    PreviewExposure? Exposure,
    IReadOnlyList<(KnownFace Face, MeasuredEyes Eyes)> Faces);

namespace PhotoGallery.Application.Ports;

/// <summary>Everything measured on one photograph's preview, ready to be kept.</summary>
/// <param name="AssetId">The photograph.</param>
/// <param name="ThumbnailName">The rendition that was read.</param>
/// <param name="PreviewWrittenUtc">When that preview had been written.</param>
/// <param name="Preview">Its sharpness and exposure.</param>
/// <param name="Faces">One measurement per face that is not ignored.</param>
public sealed record PhotoMeasurement(
    int AssetId,
    string ThumbnailName,
    DateTime PreviewWrittenUtc,
    PreviewReading Preview,
    IReadOnlyList<FaceMeasurement> Faces);

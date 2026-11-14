namespace PhotoGallery.Application.Ports;

/// <summary>
/// A photograph in a "Looks the same" set, with what has already been measured
/// on it.
/// </summary>
/// <param name="AssetId">The photograph.</param>
/// <param name="ThumbnailName">The rendition it draws itself with now.</param>
/// <param name="MeasuredThumbnailName">The rendition its measurement was read from, or null when never measured.</param>
/// <param name="MeasuredPreviewWrittenUtc">When that preview had been written, as it stood then.</param>
/// <param name="Faces">Its faces that are not ignored.</param>
public sealed record MeasuringCandidate(
    int AssetId,
    string ThumbnailName,
    string? MeasuredThumbnailName,
    DateTime? MeasuredPreviewWrittenUtc,
    IReadOnlyList<FaceToMeasure> Faces)
{
    /// <summary>
    /// Whether this photograph has to be measured again, given when its preview
    /// was last written.
    /// </summary>
    /// <param name="previewWrittenUtc">When the preview on disk was last written.</param>
    /// <param name="readEyes">Whether the eye model is installed now.</param>
    /// <remarks>
    /// Measured once and kept: again only when the preview has been rewritten
    /// (a turn, or preparing the picture again), when the face pass has found
    /// faces that have no measurement yet, or when the eye model has arrived
    /// since faces were measured without it.
    /// </remarks>
    public bool IsDue(DateTime previewWrittenUtc, bool readEyes) =>
        MeasuredThumbnailName != ThumbnailName
        || MeasuredPreviewWrittenUtc != previewWrittenUtc
        || Faces.Any(face => !face.Measured || (readEyes && !face.EyesRead));
}

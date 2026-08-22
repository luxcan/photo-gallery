namespace PhotoGallery.Application.Ports;

/// <summary>
/// Finds the faces in a cached preview and describes each one as a vector.
/// </summary>
/// <remarks>
/// One port rather than a detector and a recogniser separately: the two graphs
/// are only ever used together, and the crop that passes between them is a
/// detail of how they agree rather than something a caller should hold. The
/// weights sit behind here so that swapping them - the licensed ones are
/// research-only - stays a change of configuration.
///
/// <para>Takes a path and returns plain data, exactly as the thumbnail
/// generator does, so the pass keeps ownership of what is read and when.</para>
/// </remarks>
public interface IFaceScanner
{
    /// <summary>
    /// Every face in one preview, or <see langword="null"/> when the file could
    /// not be read or decoded.
    /// </summary>
    /// <remarks>
    /// An empty list and <see langword="null"/> mean different things and the
    /// caller must keep them apart: a photograph of a landscape has no faces and
    /// is finished with, while an unreadable file has not been looked at.
    /// </remarks>
    Task<IReadOnlyList<ScannedFace>?> ScanAsync(
        string previewPath,
        CancellationToken cancellationToken = default);
}

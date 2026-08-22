namespace PhotoGallery.Application.Ports;

/// <summary>The write side of what the face pass finds.</summary>
public interface IFaceRepository
{
    /// <summary>
    /// Records what each preview yielded and marks its photos as looked at.
    /// </summary>
    /// <remarks>
    /// Replaces rather than adds to what a photo already had. A photo is only
    /// scanned again once its bytes have changed, and the faces recorded from
    /// the previous picture describe an image that is no longer there.
    /// </remarks>
    Task SaveAsync(
        IReadOnlyList<FaceScanUpdate> updates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the boxes on some pictures to follow a turn of those pictures.
    /// </summary>
    /// <param name="width">The preview's width before the turn.</param>
    /// <param name="height">Its height before the turn.</param>
    /// <remarks>
    /// Moved rather than found again. Detecting replaces what a photo had, so
    /// re-detecting a straightened picture would delete every name confirmed on
    /// it - the user would be punished for fixing it. The vectors are untouched
    /// and stay valid: they were computed from a crop aligned on the eyes and
    /// mouth, which is upright whichever way the picture was stored.
    /// </remarks>
    Task TurnFacesAsync(
        IReadOnlyList<int> assetIds,
        int degrees,
        int width,
        int height,
        CancellationToken cancellationToken = default);
}

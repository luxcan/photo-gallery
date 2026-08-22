namespace PhotoGallery.Application.Ports;

/// <summary>
/// Turns the cached copies of one picture, without going back to the original.
/// </summary>
/// <remarks>
/// Straightening a photograph should be immediate, and the originals live on a
/// share holding 25 GB. The renditions are local, small, and already the picture
/// the app draws and the detector reads - so turning those is the whole job.
/// </remarks>
public interface IRenditionTurner
{
    /// <summary>
    /// Turns both renditions clockwise, and reports the preview's size as it was
    /// before the turn.
    /// </summary>
    /// <remarks>
    /// The size before, because the faces recorded against this picture are in
    /// those pixels and have to be moved with it. Null when there was nothing
    /// readable to turn, so the caller records no turn either.
    /// </remarks>
    TurnedRendition? Turn(string thumbnailName, int degrees);
}

/// <summary>The preview's size before it was turned, in pixels.</summary>
public readonly record struct TurnedRendition(int Width, int Height);

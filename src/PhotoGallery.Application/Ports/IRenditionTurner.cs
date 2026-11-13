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

    /// <summary>
    /// How far clockwise the cached picture must turn to show the original as its
    /// file reads now, with <paramref name="rotation"/> applied on top.
    /// </summary>
    /// <remarks>
    /// A turn made on another machine is written into the file's own tag, with
    /// the file's dates put back, so nothing a scan looks at changes and a
    /// rendition built here before that turn keeps the old way up for good. The
    /// two pictures are compared rather than the tag remembered, because no
    /// record was kept of which tag a rendition was built from.
    ///
    /// <para>Zero when they already agree, when there is no cached picture to
    /// compare, when the original is not a picture the decoder reads (a video),
    /// or when the comparison is not clear enough to act on: turning a
    /// photograph on a guess is worse than leaving it. Null only when the
    /// original's file could not be reached, so the caller can wait and ask
    /// again.</para>
    /// </remarks>
    int? DegreesBehindOriginal(string thumbnailName, string originalPath, int rotation);
}

/// <summary>The preview's size before it was turned, in pixels.</summary>
public readonly record struct TurnedRendition(int Width, int Height);

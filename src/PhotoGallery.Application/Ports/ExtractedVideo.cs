namespace PhotoGallery.Application.Ports;

/// <summary>What one open of a video yielded.</summary>
/// <remarks>
/// Everything obtainable from that open arrives together, because over the share
/// the open is the cost and a second one to ask a further question would be the
/// same price again.
/// </remarks>
/// <param name="Duration">
/// How long the clip runs, or null where the container would not say. Null is a
/// fact about the file rather than a failure: the frames are still usable, and
/// the badge simply has nothing to show.
/// </param>
/// <param name="Keyframes">
/// The frames in the order they were taken, first at the front. The first is the
/// poster. Never empty - an extractor that could not decode a single frame
/// returns null for the whole video instead.
/// </param>
public sealed record ExtractedVideo(
    TimeSpan? Duration,
    int SourceWidth,
    int SourceHeight,
    IReadOnlyList<ExtractedKeyframe> Keyframes);

namespace PhotoGallery.Application.Ports;

/// <summary>What one video yielded, ready to be recorded.</summary>
/// <remarks>
/// One update per video rather than one per frame. The frames of a clip are
/// written together or not at all: a row naming a poster whose companions were
/// never saved would look finished to every later pass, and the faces in the
/// rest of the clip would never be looked for.
/// </remarks>
/// <param name="Keyframes">
/// In order, ordinal 0 first. Ordinal 0 is the poster, and its name is what the
/// asset row comes to carry.
/// </param>
public sealed record VideoKeyframeUpdate(
    int AssetId,
    TimeSpan? Duration,
    int SourceWidth,
    int SourceHeight,
    IReadOnlyList<StoredKeyframe> Keyframes)
{
    /// <summary>The rendition the grid draws for this video.</summary>
    public string PosterName => Keyframes[0].ThumbnailName;
}

/// <summary>One frame of a video, once its renditions are on disk.</summary>
public sealed record StoredKeyframe(int Ordinal, TimeSpan Position, string ThumbnailName);

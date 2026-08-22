namespace PhotoGallery.Domain.Assets;

/// <summary>One still taken out of a video, stored as an ordinary rendition.</summary>
/// <remarks>
/// A video cannot be decoded on demand the way a photograph can - the file is
/// large, it lives over the share, and the whole point of the pass is that it is
/// read once. So the frames it yields are written into the same thumbnail store
/// the photographs use, and from that moment on nothing downstream knows or
/// cares that they came out of a video.
///
/// <para>A row per frame rather than a list on the asset, because each one is a
/// file on disk that can be missing independently, and the pass has to be able
/// to tell which of them it already made.</para>
/// </remarks>
public sealed class VideoKeyframe
{
    public int Id { get; set; }

    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    /// <summary>
    /// Which frame of the few this is: 0 first, then in the order they were
    /// taken from the clip.
    /// </summary>
    /// <remarks>
    /// Part of the rendition's name as well as its order, so the name a frame
    /// gets is settled before it is decoded and stays the same every time the
    /// video is read again. Ordinal 0 is the poster.
    /// </remarks>
    public int Ordinal { get; set; }

    /// <summary>Where in the clip this frame was taken from.</summary>
    /// <remarks>
    /// Kept because it is the only thing that explains a frame to a person
    /// looking at it - a face found nine minutes in is a different claim about a
    /// video than the same face on the poster - and because it is what a later
    /// "show me this moment" would seek to.
    /// </remarks>
    public TimeSpan Position { get; set; }

    /// <summary>File name of this frame's rendition, in the thumbnail store.</summary>
    /// <remarks>
    /// Derived from the video's content hash and this frame's ordinal, so it is
    /// stable across re-runs: reading a video again overwrites the frames it
    /// wrote before instead of leaving a second set beside them.
    /// </remarks>
    public required string ThumbnailName { get; set; }
}

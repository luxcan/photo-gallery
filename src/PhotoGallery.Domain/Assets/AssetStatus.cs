namespace PhotoGallery.Domain.Assets;

/// <summary>How far an indexed file has got through preparation.</summary>
/// <remarks>
/// Recorded rather than worked out from whether a thumbnail name is set, because
/// two of these states cannot be derived at all: a file that will never decode
/// looks exactly like one that has not been tried yet, and a video looks like a
/// photo that is still waiting. Both would otherwise be read again on every pass.
/// </remarks>
public enum AssetStatus
{
    /// <summary>Indexed, but its renditions have not been made yet.</summary>
    Pending = 0,

    /// <summary>Its renditions exist and the row names them.</summary>
    Ready = 1,

    /// <summary>
    /// The file could not be read or decoded. Kept as a fact rather than retried,
    /// so one broken file does not cost a read on every pass for the rest of time.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Nothing for the preparing pass to make.
    /// </summary>
    /// <remarks>
    /// Where a video sits until the keyframe pass reaches it. The preparing pass
    /// reads photographs and would leave 4,743 videos permanently outstanding if
    /// they were merely pending; once a clip has a poster it is
    /// <see cref="Ready"/> like anything else, and its renditions are frames
    /// rather than a decode of the file itself.
    /// </remarks>
    Skipped = 3,
}

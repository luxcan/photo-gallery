namespace PhotoGallery.Application.Ports;

/// <summary>
/// How an attempt to take frames out of a video ended.
/// </summary>
/// <remarks>
/// Three answers rather than a nullable result, for the reason
/// <see cref="CoordinateOutcome"/> has three: the two ways of coming back with
/// no frames must not be written down the same way. A container this machine has
/// no codec for is a settled answer and should never be opened again; a file
/// that could not be reached is not an answer at all.
///
/// <para>This was learned rather than designed. The pass first recorded both as
/// <c>Failed</c>, and on the real library that wrote off 24 videos in 468 - of
/// six checked by hand, five extracted perfectly on the next attempt and only
/// the zero-byte one was genuinely dead. Every one of those five would have
/// stayed blank for good.</para>
/// </remarks>
public enum KeyframeOutcome
{
    /// <summary>Frames were taken. The video is prepared.</summary>
    Extracted,

    /// <summary>
    /// The file was reached and will not decode on this machine. Settled; do not
    /// open it again.
    /// </summary>
    Undecodable,

    /// <summary>
    /// The file could not be reached, or the decoder failed in a way that says
    /// nothing about the file. Nothing is known, so nothing is recorded and the
    /// next run tries again.
    /// </summary>
    Unavailable,
}

namespace PhotoGallery.Domain.Assets;

/// <summary>Where in a clip to take its stills from.</summary>
/// <remarks>
/// Decided here, away from any decoder, because it is a judgement about videos
/// rather than a detail of how one is read - and because it is the part worth
/// pinning in a test.
/// </remarks>
public static class KeyframePlan
{
    /// <summary>How many frames a clip of any real length yields.</summary>
    /// <remarks>
    /// Three, which is what the poster and the face pass between them need. Each
    /// one costs a seek and a decode over the share, and a fourth would buy
    /// little: the people in a clip are overwhelmingly present in more than a
    /// third of it, and the ones who are not are as likely to be missed by four
    /// frames as by three. Scene detection is the answer to that question, and
    /// it is deliberately out of scope.
    /// </remarks>
    public const int FrameCount = 3;

    /// <summary>
    /// A clip shorter than this yields one frame, at the start.
    /// </summary>
    /// <remarks>
    /// Three seeks into a two-second clip land on very nearly the same picture,
    /// so they cost three decodes to learn one thing. Phone libraries carry a
    /// lot of these - a two second clip taken by mistake is still a video.
    /// </remarks>
    public static readonly TimeSpan ShortClip = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Where to seek to, in order, for a clip of <paramref name="duration"/>.
    /// </summary>
    /// <remarks>
    /// A tenth in rather than at zero, and a tenth from the end rather than at
    /// it: the first and last moments of a clip are the ones most likely to be
    /// black, blurred by a hand still moving, or a fade. The poster is the first
    /// of these, so the frame most likely to be worth looking at should be the
    /// one that arrives first.
    ///
    /// <para>An unknown or nonsensical duration yields the single frame at the
    /// start. Some containers will not say how long they are, and a clip that
    /// cannot be measured can still have its first frame taken.</para>
    /// </remarks>
    public static IReadOnlyList<TimeSpan> PositionsFor(TimeSpan? duration)
    {
        if (duration is not TimeSpan length || length <= TimeSpan.Zero || length < ShortClip)
        {
            return [TimeSpan.Zero];
        }

        return
        [
            length * 0.1,
            length * 0.5,
            length * 0.9,
        ];
    }
}

using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Domain.Faces;

/// <summary>One face found in one photo.</summary>
public sealed class Face
{
    public int Id { get; set; }

    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    public FaceBounds Bounds { get; set; }

    /// <summary>The detector's confidence, 0 to 1. Low scores are often not faces at all.</summary>
    public float DetectScore { get; set; }

    public FaceEmbedding Embedding { get; set; }

    /// <summary>
    /// When this face was set aside as nobody worth tracking, or null while it
    /// is still an open question.
    /// </summary>
    /// <remarks>
    /// Strangers in the background outnumber the people a library is about. A
    /// rejection cannot say this: it records that a face is not one particular
    /// person, which leaves it to be offered as everybody else in turn. This
    /// says it is nobody, once and for all.
    ///
    /// <para>A date rather than a flag, so it is possible to see when a sweep
    /// was made - and reversible, because a face set aside by mistake is one
    /// name away from being wanted again.</para>
    /// </remarks>
    public DateTime? IgnoredUtc { get; set; }

    /// <summary>
    /// The machine that set it aside, or empty for this library's own answer.
    /// </summary>
    /// <remarks>
    /// Empty rather than this machine's own id, so that nothing local has to
    /// remember to stamp it and a row written before this column existed is
    /// correctly attributed. It is resolved on the way out, where the machine
    /// publishing knows who it is.
    ///
    /// <para>It has to survive, because a machine publishes everything it holds
    /// rather than only what it set it asideself. An answer that lost its author
    /// passing through would be republished as the forwarder's own and would
    /// start settling ties it has no business settling - and two machines that
    /// answered in the same second would then disagree about which of them won,
    /// depending on who had passed the answer on. Three laptops would never
    /// converge.</para>
    /// </remarks>
    public Guid IgnoredBy { get; set; }
}

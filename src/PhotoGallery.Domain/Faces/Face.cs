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
}

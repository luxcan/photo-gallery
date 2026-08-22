using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.People;

/// <summary>
/// What one person looked like over one stretch of time.
/// </summary>
/// <remarks>
/// A single stored vector per person fails for children: across childhood a face
/// changes more than most adults differ from each other, so a newborn and a
/// ten-year-old do not match. Splitting a person into eras, each with its own
/// centroid, is what makes "every photo of Ana Lim" work across twelve years.
/// </remarks>
public sealed class PersonEra
{
    public int Id { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    /// <summary>Inclusive start of the era.</summary>
    public DateTime FromUtc { get; set; }

    /// <summary>Exclusive end of the era.</summary>
    public DateTime ToUtc { get; set; }

    /// <summary>Mean of the confirmed face embeddings within this era.</summary>
    public FaceEmbedding Centroid { get; set; }

    /// <summary>How many confirmed faces the centroid was averaged from.</summary>
    public int SampleCount { get; set; }

    public bool Covers(DateTime takenUtc) => takenUtc >= FromUtc && takenUtc < ToUtc;
}

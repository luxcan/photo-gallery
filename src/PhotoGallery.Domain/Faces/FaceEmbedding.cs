using PhotoGallery.Domain.Vectors;

namespace PhotoGallery.Domain.Faces;

/// <summary>
/// A face as a point in 512-dimensional space. Faces of the same person land
/// close together; the distance between two embeddings is what "looks like the
/// same person" actually means.
/// </summary>
/// <remarks>
/// Values arrive L2-normalised from the recognition model, so cosine similarity
/// reduces to a dot product. This type does the one-to-one comparison; ranking
/// one face against the whole library is a matrix operation and lives in
/// Infrastructure, where a SIMD implementation can be used.
/// </remarks>
public readonly struct FaceEmbedding : IEquatable<FaceEmbedding>
{
    public const int Dimensions = 512;

    private readonly float[]? _values;

    public FaceEmbedding(float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != Dimensions)
        {
            throw new ArgumentException(
                $"Expected {Dimensions} values but got {values.Length}.", nameof(values));
        }

        _values = values;
    }

    public bool IsEmpty => _values is null;

    public ReadOnlySpan<float> Values =>
        _values ?? throw new InvalidOperationException("The embedding has no values.");

    /// <summary>
    /// Cosine similarity, from 1.0 (identical) down through 0.0 (unrelated).
    /// Same-person pairs typically score well above 0.4; the exact threshold is
    /// a tuning decision made where matching happens, not here.
    /// </summary>
    public float SimilarityTo(in FaceEmbedding other) =>
        UnitVectors.Dot(Values, other.Values);

    /// <summary>
    /// The average of several faces, scaled back to unit length so it can be
    /// compared against a single one.
    /// </summary>
    /// <remarks>
    /// Rescaling is not decoration. The mean of several unit vectors is shorter
    /// than one - the more they disagree, the shorter - so without this a
    /// well-agreed group and a scattered one would score differently against the
    /// same face for reasons that have nothing to do with who it is.
    /// </remarks>
    public static FaceEmbedding Mean(IReadOnlyList<FaceEmbedding> embeddings)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        if (embeddings.Count == 0)
        {
            throw new ArgumentException("There is no average of no faces.", nameof(embeddings));
        }

        float[] mean = UnitVectors.Mean(
            [.. embeddings.Select(embedding => (ReadOnlyMemory<float>)embedding._values!)],
            Dimensions)
            ?? throw new ArgumentException(
                "These faces average to nothing, so they cannot be compared against.",
                nameof(embeddings));

        return new FaceEmbedding(mean);
    }

    public bool Equals(FaceEmbedding other) =>
        (_values is null && other._values is null) ||
        (_values is not null && other._values is not null && _values.AsSpan().SequenceEqual(other._values));

    public override bool Equals(object? obj) => obj is FaceEmbedding other && Equals(other);

    public override int GetHashCode() => _values is null ? 0 : _values.Length;

    public static bool operator ==(FaceEmbedding left, FaceEmbedding right) => left.Equals(right);

    public static bool operator !=(FaceEmbedding left, FaceEmbedding right) => !left.Equals(right);
}

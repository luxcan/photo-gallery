using PhotoGallery.Domain.Vectors;

namespace PhotoGallery.Domain.Search;

/// <summary>
/// A picture, or a phrase, as a point in the space CLIP puts both into.
/// </summary>
/// <remarks>
/// The one thing that makes typed search work: an image and a description of it
/// land near each other, so "birthday cake" is answered by comparing one vector
/// against every photograph rather than by anything having been tagged.
///
/// <para>A separate type from
/// <see cref="PhotoGallery.Domain.Faces.FaceEmbedding"/> on purpose, and not a
/// wider version of it. A face vector says who somebody is and this says what a
/// picture is of; they are different spaces, and the dot product between one of
/// each is a number that means nothing. Keeping them apart makes that mistake
/// fail to compile rather than quietly return 0.31.</para>
/// </remarks>
public readonly struct ContentEmbedding : IEquatable<ContentEmbedding>
{
    /// <summary>
    /// The width of the space, fixed by the model that defines it.
    /// </summary>
    /// <remarks>
    /// 768 for ViT-L/14. It is not a tuning choice: the visual and the text
    /// encoder were trained as a pair, and a vector of any other width did not
    /// come from them.
    /// </remarks>
    public const int Dimensions = 768;

    private readonly float[]? _values;

    public ContentEmbedding(float[] values)
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
    /// Cosine similarity against another point in the same space.
    /// </summary>
    /// <remarks>
    /// Values arrive scaled to unit length, so this is a dot product. The numbers
    /// are far smaller than the face model's - a good text-to-image match sits
    /// around 0.25 to 0.35 rather than near 0.9 - because the two encoders were
    /// trained to rank, not to agree. Nothing here should carry an absolute
    /// threshold; the ordering is the answer.
    /// </remarks>
    public float SimilarityTo(in ContentEmbedding other) =>
        UnitVectors.Dot(Values, other.Values);

    public bool Equals(ContentEmbedding other) =>
        (_values is null && other._values is null)
        || (_values is not null && other._values is not null
            && _values.AsSpan().SequenceEqual(other._values));

    public override bool Equals(object? obj) => obj is ContentEmbedding other && Equals(other);

    public override int GetHashCode() =>
        _values is null ? 0 : HashCode.Combine(_values.Length, _values[0], _values[^1]);

    public static bool operator ==(ContentEmbedding left, ContentEmbedding right) =>
        left.Equals(right);

    public static bool operator !=(ContentEmbedding left, ContentEmbedding right) =>
        !left.Equals(right);
}

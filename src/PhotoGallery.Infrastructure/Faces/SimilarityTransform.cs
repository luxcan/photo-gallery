namespace PhotoGallery.Infrastructure.Faces;

/// <summary>
/// A rotation, a uniform scale and a translation, as the matrix
/// <c>[[a, -b, tx], [b, a, ty]]</c>.
/// </summary>
/// <remarks>
/// Only four numbers because a similarity transform cannot shear or squash: the
/// linear part is always a scaled rotation, so <c>a</c> and <c>b</c> carry both
/// the scale (<c>sqrt(a² + b²)</c>) and the angle between them. Writing it this
/// way is what makes the least-squares fit a closed form rather than a
/// decomposition.
/// </remarks>
public readonly record struct SimilarityTransform(float A, float B, float TranslateX, float TranslateY)
{
    public float Scale => MathF.Sqrt((A * A) + (B * B));

    public (float X, float Y) Apply(float x, float y) =>
        ((A * x) - (B * y) + TranslateX, (B * x) + (A * y) + TranslateY);

    /// <summary>
    /// The reverse mapping, used to fill a destination by asking where each of
    /// its pixels came from.
    /// </summary>
    public bool TryInvert(out SimilarityTransform inverse)
    {
        float determinant = (A * A) + (B * B);
        if (determinant <= 0f || !float.IsFinite(determinant))
        {
            inverse = default;
            return false;
        }

        float a = A / determinant;
        float b = -B / determinant;

        inverse = new SimilarityTransform(
            a,
            b,
            (-a * TranslateX) + (b * TranslateY),
            (-b * TranslateX) - (a * TranslateY));

        return true;
    }
}

using System.Numerics;

namespace PhotoGallery.Domain.Vectors;

/// <summary>
/// The arithmetic every embedding in this app needs, and none of the meaning.
/// </summary>
/// <remarks>
/// Two spaces live in this codebase and a third is coming: a face vector says
/// who somebody is, a CLIP vector says what a picture is of, and the two are not
/// comparable in any sense - a dot product between them is a number with no
/// meaning at all. So they stay separate types, and only the sums they are made
/// of are shared.
///
/// <para>That is the whole reason this is a bag of functions over spans rather
/// than a vector type the others derive from. A common base class would make the
/// nonsense comparison compile; reaching for the raw spans has to be
/// deliberate.</para>
/// </remarks>
public static class UnitVectors
{
    /// <summary>
    /// The dot product, which for unit-length vectors is the cosine of the angle
    /// between them.
    /// </summary>
    /// <remarks>
    /// Widened because this is the whole cost of matching. Grouping a library's
    /// faces asks it millions of times - sixteen thousand faces against every
    /// group still open - and searching asks it once per photograph per
    /// keystroke. The hardware does eight at a time and <c>Vector&lt;T&gt;</c> is
    /// part of the runtime, so nothing is taken on to use it.
    /// </remarks>
    /// <exception cref="ArgumentException">The two are of different widths.</exception>
    public static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException(
                $"Cannot compare a vector of {left.Length} against one of {right.Length}.",
                nameof(right));
        }

        var total = Vector<float>.Zero;
        int width = Vector<float>.Count;
        int i = 0;

        for (; i <= left.Length - width; i += width)
        {
            total += new Vector<float>(left[i..]) * new Vector<float>(right[i..]);
        }

        float dot = Vector.Dot(total, Vector<float>.One);
        for (; i < left.Length; i++)
        {
            dot += left[i] * right[i];
        }

        return dot;
    }

    /// <summary>
    /// Scales a vector to unit length in place, so that comparing two of them is
    /// a plain dot product.
    /// </summary>
    /// <returns>False when the vector has no length and cannot be scaled.</returns>
    public static bool TryNormalise(float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        double sumOfSquares = 0d;
        foreach (float value in values)
        {
            sumOfSquares += (double)value * value;
        }

        double length = Math.Sqrt(sumOfSquares);
        if (length <= 0d || !double.IsFinite(length))
        {
            return false;
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (float)(values[i] / length);
        }

        return true;
    }

    /// <summary>
    /// The average of several vectors, scaled back to unit length.
    /// </summary>
    /// <remarks>
    /// Rescaling is not decoration. The mean of several unit vectors is shorter
    /// than one - the more they disagree, the shorter - so without it a
    /// well-agreed group and a scattered one would score differently against the
    /// same vector for reasons that have nothing to do with what either is.
    ///
    /// <para>Summed in double because a mean of several hundred is where single
    /// precision starts to show.</para>
    /// </remarks>
    /// <returns>Null when they average to nothing, which cannot be compared against.</returns>
    public static float[]? Mean(IReadOnlyList<ReadOnlyMemory<float>> vectors, int dimensions)
    {
        ArgumentNullException.ThrowIfNull(vectors);

        double[] totals = new double[dimensions];
        foreach (ReadOnlyMemory<float> vector in vectors)
        {
            ReadOnlySpan<float> values = vector.Span;
            for (int i = 0; i < dimensions; i++)
            {
                totals[i] += values[i];
            }
        }

        // Scaled in double rather than after rounding to float: the sum of
        // several hundred vectors is where single precision starts to show, and
        // this is the number every later comparison is made against.
        double length = 0d;
        foreach (double total in totals)
        {
            length += total * total;
        }

        length = Math.Sqrt(length);
        if (length <= 0d || !double.IsFinite(length))
        {
            return null;
        }

        float[] mean = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            mean[i] = (float)(totals[i] / length);
        }

        return mean;
    }
}

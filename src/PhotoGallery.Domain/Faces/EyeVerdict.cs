namespace PhotoGallery.Domain.Faces;

/// <summary>
/// The rule that turns one face's eye measurement into Open, Closed or Unknown.
/// </summary>
/// <remarks>
/// Every doubt lands on <see cref="EyeState.Unknown"/>. Being wrong towards
/// "closed" is what would make the app suggest a worse photograph, so Closed
/// needs both eyes plainly shut on a face that is plainly facing the camera and
/// plainly found; anything less is not counted at all. Sunglasses, a hand over
/// the face, a profile and a half-blink are all Unknown, never Closed.
///
/// <para>Pure, and holding every threshold, so the whole decision is testable
/// without a model or a picture. The numbers are not defaults: phase 1 of PRP 14
/// chooses them from the real library and they are passed in.</para>
/// </remarks>
public sealed class EyeVerdict
{
    /// <param name="closedAtMost">
    /// An eye whose openness is at or below this is shut.
    /// </param>
    /// <param name="openAtLeast">
    /// An eye whose openness is at or above this is open. Must be above
    /// <paramref name="closedAtMost"/>; between the two is Unknown.
    /// </param>
    /// <param name="mostTurn">
    /// A face turned further than this from the camera is Unknown.
    /// </param>
    /// <param name="leastConfidence">
    /// Landmarks that agree with the detector less than this are Unknown.
    /// </param>
    public EyeVerdict(double closedAtMost, double openAtLeast, double mostTurn, double leastConfidence)
    {
        // Written as negated comparisons so that a NaN, which fails every
        // comparison, is refused rather than slipping through as a threshold
        // nothing ever meets.
        if (!(closedAtMost >= 0d))
        {
            throw new ArgumentOutOfRangeException(
                nameof(closedAtMost), closedAtMost, "An openness cannot be negative.");
        }

        if (!(openAtLeast > closedAtMost) || !double.IsFinite(openAtLeast))
        {
            throw new ArgumentOutOfRangeException(
                nameof(openAtLeast),
                openAtLeast,
                "Open has to start above where closed ends, or one eye could be both.");
        }

        if (!(mostTurn > 0d) || !double.IsFinite(mostTurn))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mostTurn), mostTurn, "Allowing no turn at all would judge no face.");
        }

        if (!(leastConfidence is >= 0d and <= 1d))
        {
            throw new ArgumentOutOfRangeException(
                nameof(leastConfidence), leastConfidence, "Confidence is a share, from 0 to 1.");
        }

        ClosedAtMost = closedAtMost;
        OpenAtLeast = openAtLeast;
        MostTurn = mostTurn;
        LeastConfidence = leastConfidence;
    }

    /// <summary>
    /// The thresholds phase 1 of PRP 14 chose from the real library, where the
    /// numbers behind each are recorded.
    /// </summary>
    /// <remarks>
    /// Closed at 0.060 because no hand-labelled open face measured below 0.110
    /// on its wider eye, while the can't-tell squints sat just above (0.062 to
    /// 0.081). Open from 0.15, above every closed face (0.148 at most), and only
    /// ever shown: Open is never counted. A turn of 0.40 is a chosen guard, not
    /// a measurement - nothing labelled lay between 0.357 and 0.467 to pin it -
    /// and confidence 0.60 refuses the profiles at zero while keeping sleeping
    /// babies lying sideways (0.73 to 0.77).
    /// </remarks>
    public static EyeVerdict Measured { get; } = new(
        closedAtMost: 0.060, openAtLeast: 0.15, mostTurn: 0.40, leastConfidence: 0.60);

    public double ClosedAtMost { get; }

    public double OpenAtLeast { get; }

    public double MostTurn { get; }

    public double LeastConfidence { get; }

    /// <summary>What the measurement says about the eyes, or Unknown when in doubt.</summary>
    public EyeState Judge(EyeMeasurement measurement)
    {
        // A face turned away, or landmarks the detector does not agree with,
        // say nothing about the eyes whatever the eyes measured. Negated so an
        // unknown turn (NaN) is a doubt too.
        if (!(measurement.Turn <= MostTurn) || !(measurement.Confidence >= LeastConfidence))
        {
            return EyeState.Unknown;
        }

        // An eye that could not be measured is most often one the face itself
        // hides: one eye alone is not enough to call either way.
        if (measurement.LeftOpenness is not double left || !double.IsFinite(left)
            || measurement.RightOpenness is not double right || !double.IsFinite(right))
        {
            return EyeState.Unknown;
        }

        if (left <= ClosedAtMost && right <= ClosedAtMost)
        {
            return EyeState.Closed;
        }

        // Anything else short of both plainly open - a wink, a half blink, one
        // eye read wrongly - is Unknown rather than either answer.
        return left >= OpenAtLeast && right >= OpenAtLeast
            ? EyeState.Open
            : EyeState.Unknown;
    }
}

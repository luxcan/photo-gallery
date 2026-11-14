using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// The eye rule at each of its edges. Being wrong towards "closed" is what would
/// make the app suggest a worse photograph, so most of these are about the
/// cases that must come out Unknown rather than Closed.
/// </summary>
public sealed class EyeVerdictTests
{
    // Illustrative numbers, not the ones phase 1 will choose: the rule is what is
    // under test here, and every threshold is passed in.
    private const double ClosedAtMost = 0.10;
    private const double OpenAtLeast = 0.20;
    private const double MostTurn = 0.50;
    private const double LeastConfidence = 0.70;

    private static readonly EyeVerdict s_verdict =
        new(ClosedAtMost, OpenAtLeast, MostTurn, LeastConfidence);

    [Fact]
    public void Judge_BothEyesShutOnAFacingFaceIsClosed() =>
        Assert.Equal(EyeState.Closed, s_verdict.Judge(Facing(0.05, 0.04)));

    [Fact]
    public void Judge_BothEyesExactlyAtTheClosedThresholdIsClosed() =>
        Assert.Equal(EyeState.Closed, s_verdict.Judge(Facing(ClosedAtMost, ClosedAtMost)));

    [Fact]
    public void Judge_AnEyeJustAboveTheClosedThresholdIsNotClosed() =>
        Assert.Equal(EyeState.Unknown, s_verdict.Judge(Facing(ClosedAtMost, ClosedAtMost + 0.001)));

    [Fact]
    public void Judge_BothEyesWideOnAFacingFaceIsOpen() =>
        Assert.Equal(EyeState.Open, s_verdict.Judge(Facing(0.30, 0.28)));

    [Fact]
    public void Judge_BothEyesExactlyAtTheOpenThresholdIsOpen() =>
        Assert.Equal(EyeState.Open, s_verdict.Judge(Facing(OpenAtLeast, OpenAtLeast)));

    [Fact]
    public void Judge_AnEyeJustBelowTheOpenThresholdIsNotOpen() =>
        Assert.Equal(EyeState.Unknown, s_verdict.Judge(Facing(OpenAtLeast - 0.001, 0.30)));

    [Fact]
    public void Judge_EyesBetweenTheThresholdsAreUnknown() =>
        Assert.Equal(EyeState.Unknown, s_verdict.Judge(Facing(0.15, 0.15)));

    [Fact]
    public void Judge_OneEyeShutAndOneOpenIsUnknown()
    {
        // A wink, or one eye read wrongly: either way not two eyes plainly shut.
        Assert.Equal(EyeState.Unknown, s_verdict.Judge(Facing(0.05, 0.30)));
        Assert.Equal(EyeState.Unknown, s_verdict.Judge(Facing(0.30, 0.05)));
    }

    [Fact]
    public void Judge_ALeftEyeThatCouldNotBeMeasuredIsUnknown() =>
        Assert.Equal(EyeState.Unknown, s_verdict.Judge(Facing(null, 0.04)));

    [Fact]
    public void Judge_ARightEyeThatCouldNotBeMeasuredIsUnknown() =>
        Assert.Equal(EyeState.Unknown, s_verdict.Judge(Facing(0.04, null)));

    [Fact]
    public void Judge_AnOpennessThatIsNotANumberIsUnknown() =>
        Assert.Equal(EyeState.Unknown, s_verdict.Judge(Facing(double.NaN, 0.04)));

    [Fact]
    public void Judge_AFaceTurnedExactlyToTheLimitIsStillJudged() =>
        Assert.Equal(
            EyeState.Closed,
            s_verdict.Judge(new EyeMeasurement(0.05, 0.05, MostTurn, 0.95)));

    [Fact]
    public void Judge_AFaceTurnedPastTheLimitIsUnknownEvenWithBothEyesShut() =>
        Assert.Equal(
            EyeState.Unknown,
            s_verdict.Judge(new EyeMeasurement(0.05, 0.05, MostTurn + 0.001, 0.95)));

    [Fact]
    public void Judge_AFaceWhoseTurnIsUnknownIsUnknown() =>
        Assert.Equal(
            EyeState.Unknown,
            s_verdict.Judge(new EyeMeasurement(0.05, 0.05, double.NaN, 0.95)));

    [Fact]
    public void Judge_LandmarksExactlyAtTheLeastConfidenceAreStillJudged() =>
        Assert.Equal(
            EyeState.Open,
            s_verdict.Judge(new EyeMeasurement(0.30, 0.30, 0.1, LeastConfidence)));

    [Fact]
    public void Judge_LandmarksLessSureThanTheLimitAreUnknownEvenWithBothEyesShut() =>
        Assert.Equal(
            EyeState.Unknown,
            s_verdict.Judge(new EyeMeasurement(0.05, 0.05, 0.1, LeastConfidence - 0.001)));

    [Fact]
    public void Judge_AFaceNothingWasMeasuredOnIsUnknown() =>
        Assert.Equal(EyeState.Unknown, s_verdict.Judge(EyeMeasurement.Unmeasured));

    [Theory]
    [InlineData(0.20, 0.20, 0.5, 0.7)]    // no band between closed and open
    [InlineData(0.25, 0.20, 0.5, 0.7)]    // closed above open
    [InlineData(-0.01, 0.20, 0.5, 0.7)]   // a negative openness means nothing
    [InlineData(0.10, 0.20, 0.0, 0.7)]    // no turn allowed at all would judge nothing
    [InlineData(0.10, 0.20, 0.5, 1.01)]   // confidence is a share
    [InlineData(0.10, 0.20, 0.5, -0.01)]
    [InlineData(double.NaN, 0.20, 0.5, 0.7)]
    [InlineData(0.10, double.NaN, 0.5, 0.7)]
    [InlineData(0.10, 0.20, double.NaN, 0.7)]
    [InlineData(0.10, 0.20, 0.5, double.NaN)]
    public void Constructor_RefusesThresholdsThatCannotMakeSense(
        double closedAtMost, double openAtLeast, double mostTurn, double leastConfidence) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EyeVerdict(closedAtMost, openAtLeast, mostTurn, leastConfidence));

    [Fact]
    public void Constructor_KeepsTheThresholdsItWasGiven()
    {
        Assert.Equal(ClosedAtMost, s_verdict.ClosedAtMost);
        Assert.Equal(OpenAtLeast, s_verdict.OpenAtLeast);
        Assert.Equal(MostTurn, s_verdict.MostTurn);
        Assert.Equal(LeastConfidence, s_verdict.LeastConfidence);
    }

    [Fact]
    public void Measured_HoldsTheThresholdsPhaseOneChose()
    {
        // PRP 14, phase 1: of 60 hand-labelled known faces no open face reads
        // Closed at these settings. They are recorded there with their numbers.
        Assert.Equal(0.060, EyeVerdict.Measured.ClosedAtMost);
        Assert.Equal(0.15, EyeVerdict.Measured.OpenAtLeast);
        Assert.Equal(0.40, EyeVerdict.Measured.MostTurn);
        Assert.Equal(0.60, EyeVerdict.Measured.LeastConfidence);
    }

    [Theory]
    [InlineData(0.060, 0.060, 0.40, 0.60, EyeState.Closed)]   // every limit met exactly
    [InlineData(0.060, 0.061, 0.40, 0.60, EyeState.Unknown)]  // one eye just open enough to doubt
    [InlineData(0.15, 0.15, 0.40, 0.60, EyeState.Open)]
    [InlineData(0.15, 0.149, 0.40, 0.60, EyeState.Unknown)]
    [InlineData(0.05, 0.05, 0.401, 0.60, EyeState.Unknown)]   // turned just past the limit
    [InlineData(0.05, 0.05, 0.40, 0.599, EyeState.Unknown)]   // landmarks just too unsure
    public void Measured_JudgesEachEdgeAsRecorded(
        double left, double right, double turn, double confidence, EyeState expected) =>
        Assert.Equal(expected, EyeVerdict.Measured.Judge(new EyeMeasurement(left, right, turn, confidence)));

    [Fact]
    public void Measured_LowLidsReadAsClosedStayClosed()
    {
        // Looking down counts as closed: fully lowered lids that measure shut
        // are not turned into Unknown by any special rule. The paired step is
        // what keeps one from deciding a set unless the same person is open in
        // another copy.
        Assert.Equal(EyeState.Closed, EyeVerdict.Measured.Judge(new EyeMeasurement(0.02, 0.03, 0.10, 0.90)));
    }

    private static EyeMeasurement Facing(double? left, double? right) =>
        new(left, right, Turn: 0.05, Confidence: 0.95);
}

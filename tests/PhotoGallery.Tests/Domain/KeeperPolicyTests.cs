using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// The keeper ladder of PRP 14, one rung at a time and at each margin's edge.
/// </summary>
/// <remarks>
/// Every copy here is built from numbers alone, which is the point of keeping
/// the ladder pure: the whole decision can be pinned down without a model, a
/// picture or a folder.
/// </remarks>
public sealed class KeeperPolicyTests
{
    private const int Mum = 1;
    private const int Ana = 2;
    private static readonly DateTime s_shot = new(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Choose_EmptySetThrows() =>
        Assert.Throws<ArgumentException>(() => KeeperPolicy.Choose([]));

    [Fact]
    public void Choose_ASingleCopyIsTheKeeperWithNothingDeciding()
    {
        KeeperChoice choice = KeeperPolicy.Choose([Copy(1)]);

        Assert.Equal(1, choice.KeeperId);
        Assert.Null(choice.DecidedBy);
    }

    [Fact]
    public void Choose_AFullTieSuggestsNothing()
    {
        KeeperChoice choice = KeeperPolicy.Choose([Copy(1), Copy(2)]);

        Assert.Null(choice.KeeperId);
        Assert.Null(choice.DecidedBy);
        Assert.All(choice.Copies, verdict => Assert.Null(verdict.LostOn));
    }

    [Fact]
    public void Choose_GivesTheSameAnswerWhateverOrderTheCopiesArriveIn()
    {
        CopyQuality sharp = Copy(1, sharpness: 300);
        CopyQuality soft = Copy(2, sharpness: 100);

        Assert.Equal(1, KeeperPolicy.Choose([sharp, soft]).KeeperId);
        Assert.Equal(1, KeeperPolicy.Choose([soft, sharp]).KeeperId);
    }

    // --- 1. Eyes ------------------------------------------------------------

    [Fact]
    public void Eyes_TheOpenFrameWinsWhateverTheSharpnessSizeOrBytes()
    {
        CopyQuality blink = Copy(1, sharpness: 900, pixels: 12_000_000,
            faces: [Face(10, Mum, EyeState.Closed)]);
        CopyQuality open = Copy(2, sharpness: 100, pixels: 3_000_000,
            faces: [Face(20, Mum, EyeState.Open)]);

        KeeperChoice choice = KeeperPolicy.Choose([blink, open]);

        Assert.Equal(2, choice.KeeperId);
        Assert.Equal(KeeperCriterion.Eyes, choice.DecidedBy);
        Assert.Equal(KeeperCriterion.Eyes, choice.For(1).LostOn);
        Assert.Equal(10, Assert.Single(choice.For(1).ClosedFaces).FaceId);
        Assert.Empty(choice.For(2).ClosedFaces);
    }

    [Fact]
    public void Eyes_ClosedAgainstUnknownIsNoDifference()
    {
        // A lowered gaze or a squint reads Closed in one frame and Unknown in
        // the next. Counting it decided a set in phase 1 for a shaken frame.
        CopyQuality closed = Copy(1, sharpness: 300, faces: [Face(10, Mum, EyeState.Closed)]);
        CopyQuality unknown = Copy(2, sharpness: 100, faces: [Face(20, Mum, EyeState.Unknown)]);

        KeeperChoice choice = KeeperPolicy.Choose([closed, unknown]);

        Assert.Equal(KeeperCriterion.Sharpness, choice.DecidedBy);
        Assert.Equal(1, choice.KeeperId);
        Assert.Empty(choice.For(1).ClosedFaces);
    }

    [Fact]
    public void Eyes_ClosedInEveryCopyIsNoDifference()
    {
        CopyQuality first = Copy(1, faces: [Face(10, Mum, EyeState.Closed)]);
        CopyQuality second = Copy(2, faces: [Face(20, Mum, EyeState.Closed)]);

        Assert.Null(KeeperPolicy.Choose([first, second]).KeeperId);
    }

    [Fact]
    public void Eyes_OnlyTheSamePersonPairs()
    {
        // Mum closed here and Ana open there says nothing about either of them.
        CopyQuality first = Copy(1, faces: [Face(10, Mum, EyeState.Closed)]);
        CopyQuality second = Copy(2, faces: [Face(20, Ana, EyeState.Open)]);

        Assert.Null(KeeperPolicy.Choose([first, second]).KeeperId);
    }

    [Fact]
    public void Eyes_UnreadEyesNeverDecide()
    {
        CopyQuality first = Copy(1, faces: [Face(10, Mum, eyes: null)]);
        CopyQuality second = Copy(2, faces: [Face(20, Mum, EyeState.Open)]);

        Assert.Null(KeeperPolicy.Choose([first, second]).KeeperId);
    }

    [Fact]
    public void Eyes_AreSkippedWhenTheEyeModelIsMissing()
    {
        CopyQuality blink = Copy(1, sharpness: 300, faces: [Face(10, Mum, EyeState.Closed)]);
        CopyQuality open = Copy(2, sharpness: 100, faces: [Face(20, Mum, EyeState.Open)]);

        KeeperChoice choice = KeeperPolicy.Choose([blink, open], readEyes: false);

        Assert.Equal(KeeperCriterion.Sharpness, choice.DecidedBy);
        Assert.Equal(1, choice.KeeperId);
        Assert.Empty(choice.For(1).ClosedFaces);
    }

    [Fact]
    public void Eyes_KeepTheCopiesWithTheFewestPairedBlinks()
    {
        // Mum blinks in 1, Ana blinks in 2, both blink in 3: 3 is the only copy
        // worse than another on eyes, and the other two go on to sharpness.
        CopyQuality one = Copy(1, sharpness: 300,
            faces: [Face(10, Mum, EyeState.Closed), Face(11, Ana, EyeState.Open)]);
        CopyQuality two = Copy(2, sharpness: 100,
            faces: [Face(20, Mum, EyeState.Open), Face(21, Ana, EyeState.Closed)]);
        CopyQuality three = Copy(3, sharpness: 900,
            faces: [Face(30, Mum, EyeState.Closed), Face(31, Ana, EyeState.Closed)]);

        KeeperChoice choice = KeeperPolicy.Choose([one, two, three]);

        Assert.Equal(KeeperCriterion.Eyes, choice.For(3).LostOn);
        Assert.Equal(2, choice.For(3).ClosedFaces.Count);
        Assert.Equal(1, choice.KeeperId);
        Assert.Equal(KeeperCriterion.Sharpness, choice.DecidedBy);

        // 1 and 2 did not lose on eyes, so neither is marked for a blink.
        Assert.Empty(choice.For(1).ClosedFaces);
        Assert.Empty(choice.For(2).ClosedFaces);
    }

    [Fact]
    public void Eyes_ATieMarksNoCopy()
    {
        // Mum blinks in 1 and Ana in 2: one each, so eyes say nothing and
        // sharpness decides. A marker on either would claim eyes had spoken.
        CopyQuality one = Copy(1, sharpness: 900,
            faces: [Face(10, Mum, EyeState.Closed), Face(11, Ana, EyeState.Open)]);
        CopyQuality two = Copy(2, sharpness: 100,
            faces: [Face(20, Mum, EyeState.Open), Face(21, Ana, EyeState.Closed)]);

        KeeperChoice choice = KeeperPolicy.Choose([one, two]);

        Assert.Equal(1, choice.KeeperId);
        Assert.Equal(KeeperCriterion.Sharpness, choice.DecidedBy);
        Assert.Empty(choice.For(1).ClosedFaces);
        Assert.Empty(choice.For(2).ClosedFaces);
    }

    // --- 2. Sharpness -------------------------------------------------------

    [Fact]
    public void Sharpness_ExactlyAtTheMarginIsATie()
    {
        CopyQuality sharp = Copy(1, sharpness: 100 * KeeperPolicy.SharpnessMargin);
        CopyQuality soft = Copy(2, sharpness: 100);

        Assert.Null(KeeperPolicy.Choose([sharp, soft]).KeeperId);
    }

    [Fact]
    public void Sharpness_JustPastTheMarginDecides()
    {
        CopyQuality sharp = Copy(1, sharpness: (100 * KeeperPolicy.SharpnessMargin) + 0.01);
        CopyQuality soft = Copy(2, sharpness: 100);

        KeeperChoice choice = KeeperPolicy.Choose([sharp, soft]);

        Assert.Equal(1, choice.KeeperId);
        Assert.Equal(KeeperCriterion.Sharpness, choice.DecidedBy);
        Assert.Equal(KeeperCriterion.Sharpness, choice.For(2).LostOn);
    }

    [Fact]
    public void Sharpness_ACopyWithNoReadingNeverLosesOnIt()
    {
        // A re-saved copy smaller than the common size has no reading at all: it
        // cannot be compared at the same size, so it is not compared. The
        // original then wins it on pixels, never on sharpness.
        CopyQuality original = Copy(1, sharpness: 800, pixels: 12_000_000);
        CopyQuality small = Copy(2, sharpness: null, pixels: 300_000);

        KeeperChoice choice = KeeperPolicy.Choose([original, small]);

        Assert.Equal(1, choice.KeeperId);
        Assert.Equal(KeeperCriterion.Pixels, choice.DecidedBy);
        Assert.Equal(KeeperCriterion.Pixels, choice.For(2).LostOn);
    }

    [Fact]
    public void Sharpness_IsReadOverThePeopleEveryCopyShows()
    {
        // The scene says copy 2 is far sharper; Mum's face, in both, says copy 1
        // is. The face is what a person would judge the photograph by.
        CopyQuality one = Copy(1, sharpness: 100, faces: [Face(10, Mum, EyeState.Open, sharpness: 400)]);
        CopyQuality two = Copy(2, sharpness: 900, faces: [Face(20, Mum, EyeState.Open, sharpness: 100)]);

        KeeperChoice choice = KeeperPolicy.Choose([one, two]);

        Assert.Equal(1, choice.KeeperId);
        Assert.Equal(KeeperCriterion.Sharpness, choice.DecidedBy);
    }

    [Fact]
    public void Sharpness_AnExtraFaceInOneCopyDoesNotCount()
    {
        // Phase 1: pooling every known face made a copy lose for holding one
        // more, smaller face (40 copies). Only the people in every copy count.
        CopyQuality one = Copy(1, faces:
        [
            Face(10, Mum, EyeState.Open, sharpness: 300),
            Face(11, Ana, EyeState.Open, sharpness: 10, width: 20),
        ]);
        CopyQuality two = Copy(2, faces: [Face(20, Mum, EyeState.Open, sharpness: 300)]);

        Assert.Null(KeeperPolicy.Choose([one, two]).KeeperId);
    }

    [Fact]
    public void Sharpness_FallsBackToTheSceneWhenNotEveryCopyShowsAKnownFace()
    {
        CopyQuality one = Copy(1, sharpness: 400, faces: [Face(10, Mum, EyeState.Open, sharpness: 50)]);
        CopyQuality two = Copy(2, sharpness: 100);

        Assert.Equal(1, KeeperPolicy.Choose([one, two]).KeeperId);
    }

    [Fact]
    public void Sharpness_ACopyWithAnUnmeasuredFaceStaysInWithoutTurningTheOthersToTheScene()
    {
        // Mum in all three. 1 has a sharp background behind a blurred face, 2 a
        // sharp face, 3 no face reading at all. 3 must not send 1 and 2 back to
        // their backgrounds: the faces decide between them and 3 goes on.
        CopyQuality one = Copy(1, sharpness: 900, faces: [Face(10, Mum, EyeState.Open, sharpness: 50)]);
        CopyQuality two = Copy(2, sharpness: 100, faces: [Face(20, Mum, EyeState.Open, sharpness: 400)]);
        CopyQuality three = Copy(3, sharpness: 200, pixels: 500_000,
            faces: [Face(30, Mum, EyeState.Open, sharpness: null)]);

        KeeperChoice choice = KeeperPolicy.Choose([one, two, three]);

        Assert.Equal(KeeperCriterion.Sharpness, choice.For(1).LostOn);
        Assert.Equal(KeeperCriterion.Pixels, choice.For(3).LostOn);
        Assert.Equal(2, choice.KeeperId);
    }

    [Fact]
    public void Sharpness_PoolsAPersonsFacesByArea()
    {
        // A large face counts for more of the picture than a small one.
        CopyQuality one = Copy(1, faces:
        [
            Face(10, Mum, EyeState.Open, sharpness: 300, width: 100),
            Face(11, Ana, EyeState.Open, sharpness: 300, width: 10),
        ]);
        CopyQuality two = Copy(2, faces:
        [
            Face(20, Mum, EyeState.Open, sharpness: 300, width: 100),
            Face(21, Ana, EyeState.Open, sharpness: 1, width: 10),
        ]);

        // Ana's small face is 1% of the pooled area, so 2 is within the margin.
        Assert.Null(KeeperPolicy.Choose([one, two]).KeeperId);
    }

    // --- 3. Exposure --------------------------------------------------------

    [Fact]
    public void Exposure_ExactlyAtTheMarginIsATie()
    {
        CopyQuality bright = Copy(1, high: 0.010 + KeeperPolicy.ExposureMargin);
        CopyQuality fine = Copy(2, high: 0.010);

        Assert.Null(KeeperPolicy.Choose([bright, fine]).KeeperId);
    }

    [Fact]
    public void Exposure_JustPastTheMarginIsTooBright()
    {
        CopyQuality bright = Copy(1, high: 0.011 + KeeperPolicy.ExposureMargin);
        CopyQuality fine = Copy(2, high: 0.010);

        KeeperChoice choice = KeeperPolicy.Choose([bright, fine]);

        Assert.Equal(2, choice.KeeperId);
        Assert.Equal(KeeperCriterion.Exposure, choice.DecidedBy);
        Assert.Equal(ExposureFault.TooBright, choice.For(1).Exposure);
    }

    [Fact]
    public void Exposure_JustPastTheMarginIsTooDark()
    {
        CopyQuality dark = Copy(1, low: 0.011 + KeeperPolicy.ExposureMargin);
        CopyQuality fine = Copy(2, low: 0.010);

        KeeperChoice choice = KeeperPolicy.Choose([dark, fine]);

        Assert.Equal(2, choice.KeeperId);
        Assert.Equal(ExposureFault.TooDark, choice.For(1).Exposure);
    }

    [Fact]
    public void Exposure_TwoCopiesEachWrongOneWayIsATie()
    {
        CopyQuality bright = Copy(1, high: 0.20, low: 0.00);
        CopyQuality dark = Copy(2, high: 0.00, low: 0.20);

        Assert.Null(KeeperPolicy.Choose([bright, dark]).KeeperId);
    }

    // --- 4. Original over re-saved -------------------------------------------

    [Fact]
    public void Original_ACaptureDateBeatsNeither()
    {
        CopyQuality original = Copy(1, taken: s_shot);
        CopyQuality resaved = Copy(2);

        KeeperChoice choice = KeeperPolicy.Choose([original, resaved]);

        Assert.Equal(1, choice.KeeperId);
        Assert.Equal(KeeperCriterion.Original, choice.DecidedBy);
    }

    [Fact]
    public void Original_CoordinatesAloneCount()
    {
        CopyQuality original = Copy(1, coordinates: true);
        CopyQuality resaved = Copy(2);

        Assert.Equal(KeeperCriterion.Original, KeeperPolicy.Choose([original, resaved]).DecidedBy);
    }

    [Fact]
    public void Original_MoreDetailsIsNotBetter()
    {
        // Phase 1: in two of five sets the copy with coordinates was the smaller.
        CopyQuality dated = Copy(1, taken: s_shot, pixels: 12_000_000);
        CopyQuality datedAndPlaced = Copy(2, taken: s_shot, coordinates: true, pixels: 3_000_000);

        Assert.Equal(KeeperCriterion.Pixels, KeeperPolicy.Choose([dated, datedAndPlaced]).DecidedBy);
    }

    // --- 5. Pixels ----------------------------------------------------------

    [Fact]
    public void Pixels_MoreWins()
    {
        KeeperChoice choice = KeeperPolicy.Choose([Copy(1, pixels: 999), Copy(2, pixels: 1000)]);

        Assert.Equal(2, choice.KeeperId);
        Assert.Equal(KeeperCriterion.Pixels, choice.DecidedBy);
    }

    // --- After the last rung --------------------------------------------------

    // There is no rung after pixels: file size cannot tell one photograph saved
    // twice from two frames of a burst, so copies the rungs above cannot tell
    // apart are left to the person, whatever their capture times say.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(null)]
    public void CopiesNoRungCanTellApart_GetNoSuggestion(int? secondsApart)
    {
        DateTime? second = secondsApart is int seconds ? s_shot.AddSeconds(seconds) : null;
        DateTime? first = secondsApart is null ? null : s_shot;

        KeeperChoice choice = KeeperPolicy.Choose([Copy(1, taken: first), Copy(2, taken: second)]);

        Assert.Null(choice.KeeperId);
        Assert.Null(choice.DecidedBy);
        Assert.All(choice.Copies, verdict => Assert.Null(verdict.LostOn));
    }

    private static CopyQuality Copy(
        int assetId,
        double? sharpness = 200,
        long pixels = 1_000_000,
        DateTime? taken = null,
        bool coordinates = false,
        double high = 0.01,
        double low = 0.01,
        IReadOnlyList<CopyFace>? faces = null) =>
        new(assetId, pixels, taken, coordinates, sharpness, high, low, faces ?? []);

    private static CopyFace Face(
        int faceId, int personId, EyeState? eyes, double? sharpness = null, int width = 100) =>
        new(faceId, personId, personId == Mum ? "Mum" : "Ana",
            new FaceBounds(0, 0, width, width), eyes, sharpness);
}

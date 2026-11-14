using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The geometry under the eye rule, on hand-placed points so the expected
/// answers are arithmetic.
/// </summary>
public sealed class EyeOutlineTests
{
    [Fact]
    public void Rings_AreTheIndicesConfirmedOnRealFaces()
    {
        // Pinned because they were read off drawn crops, not derived: an edit
        // here would quietly measure a cheek or an eyebrow instead of an eye.
        Assert.Equal([35, 36, 33, 37, 39, 42, 40, 41], EyeOutline.PictureLeft);
        Assert.Equal([89, 90, 87, 91, 93, 96, 94, 95], EyeOutline.PictureRight);
        Assert.Equal(38, EyeOutline.PictureLeftCentre);
        Assert.Equal(92, EyeOutline.PictureRightCentre);
    }

    [Fact]
    public void Openness_IsTheLidGapOverTheWidth()
    {
        // Ten wide, lids two above and two below the corner line: a gap of 4.
        float[] landmarks = WithEye(EyeOutline.PictureLeft, 100f, 50f, width: 10f, halfGap: 2f, angle: 0f);

        Assert.Equal(0.4, EyeOutline.Openness(landmarks, EyeOutline.PictureLeft)!.Value, 4);
    }

    [Fact]
    public void Openness_OfAShutEyeIsZero()
    {
        float[] landmarks = WithEye(EyeOutline.PictureRight, 100f, 50f, width: 10f, halfGap: 0f, angle: 0f);

        Assert.Equal(0d, EyeOutline.Openness(landmarks, EyeOutline.PictureRight)!.Value, 6);
    }

    [Fact]
    public void Openness_DoesNotChangeWhenTheEyeIsTilted()
    {
        // A head on one side tilts the eye; the lids are measured across the
        // eye's own axis, so the tilt must not read as a wider gap.
        float[] level = WithEye(EyeOutline.PictureLeft, 100f, 50f, 10f, 2f, angle: 0f);
        float[] tilted = WithEye(EyeOutline.PictureLeft, 100f, 50f, 10f, 2f, angle: 0.6f);

        Assert.Equal(
            EyeOutline.Openness(level, EyeOutline.PictureLeft)!.Value,
            EyeOutline.Openness(tilted, EyeOutline.PictureLeft)!.Value,
            4);
    }

    [Fact]
    public void Openness_DoesNotChangeWithTheSizeOfTheFace()
    {
        float[] small = WithEye(EyeOutline.PictureLeft, 100f, 50f, 6f, 1.2f, 0f);
        float[] large = WithEye(EyeOutline.PictureLeft, 100f, 50f, 60f, 12f, 0f);

        Assert.Equal(
            EyeOutline.Openness(small, EyeOutline.PictureLeft)!.Value,
            EyeOutline.Openness(large, EyeOutline.PictureLeft)!.Value,
            4);
    }

    [Fact]
    public void Openness_OfAnEyeWithNoWidthIsUnknown()
    {
        float[] landmarks = WithEye(EyeOutline.PictureLeft, 100f, 50f, width: 0f, halfGap: 2f, angle: 0f);

        Assert.Null(EyeOutline.Openness(landmarks, EyeOutline.PictureLeft));
    }

    [Fact]
    public void Openness_OfPointsThatAreNotNumbersIsUnknown()
    {
        float[] landmarks = WithEye(EyeOutline.PictureLeft, 100f, 50f, 10f, 2f, 0f);
        landmarks[EyeOutline.PictureLeft[2] * 2] = float.NaN;

        Assert.Null(EyeOutline.Openness(landmarks, EyeOutline.PictureLeft));
    }

    [Fact]
    public void Openness_RefusesAnythingButAll106Points() =>
        Assert.Throws<ArgumentException>(
            () => EyeOutline.Openness(new float[10], EyeOutline.PictureLeft));

    [Fact]
    public void Agreement_IsOneWhenTheCentresSitOnTheDetectorsEyes()
    {
        float[] landmarks = new float[212];
        Place(landmarks, EyeOutline.PictureLeftCentre, 40f, 50f);
        Place(landmarks, EyeOutline.PictureRightCentre, 80f, 50f);

        Assert.Equal(1d, EyeOutline.Agreement(landmarks, Detector(40f, 50f, 80f, 50f)), 6);
    }

    [Fact]
    public void Agreement_FallsWithTheDistanceAsAShareOfTheEyeSpread()
    {
        // Each centre a quarter of the 40px eye spread away: agreement 0.75.
        float[] landmarks = new float[212];
        Place(landmarks, EyeOutline.PictureLeftCentre, 40f, 60f);
        Place(landmarks, EyeOutline.PictureRightCentre, 80f, 40f);

        Assert.Equal(0.75, EyeOutline.Agreement(landmarks, Detector(40f, 50f, 80f, 50f)), 6);
    }

    [Fact]
    public void Agreement_IsNeverBelowZero()
    {
        float[] landmarks = new float[212];
        Place(landmarks, EyeOutline.PictureLeftCentre, 400f, 500f);
        Place(landmarks, EyeOutline.PictureRightCentre, 800f, 500f);

        Assert.Equal(0d, EyeOutline.Agreement(landmarks, Detector(40f, 50f, 80f, 50f)));
    }

    [Fact]
    public void Agreement_WhenTheDetectorsEyesAreInOnePlaceIsZero()
    {
        float[] landmarks = new float[212];

        Assert.Equal(0d, EyeOutline.Agreement(landmarks, Detector(40f, 50f, 40f, 50f)));
    }

    /// <summary>
    /// 106 points with one eye's ring laid out: corners at either end of the
    /// width, three lower-lid points below the line and three upper above it.
    /// </summary>
    private static float[] WithEye(
        IReadOnlyList<int> ring, float centreX, float centreY, float width, float halfGap, float angle)
    {
        float[] landmarks = new float[212];
        (float X, float Y)[] local =
        [
            (-width / 2f, 0f),
            (-width / 4f, halfGap), (0f, halfGap), (width / 4f, halfGap),
            (width / 2f, 0f),
            (width / 4f, -halfGap), (0f, -halfGap), (-width / 4f, -halfGap),
        ];

        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        for (int i = 0; i < ring.Count; i++)
        {
            (float x, float y) = local[i];
            Place(landmarks, ring[i], centreX + (x * cos) - (y * sin), centreY + (x * sin) + (y * cos));
        }

        return landmarks;
    }

    private static void Place(float[] landmarks, int index, float x, float y)
    {
        landmarks[index * 2] = x;
        landmarks[(index * 2) + 1] = y;
    }

    private static float[] Detector(float leftX, float leftY, float rightX, float rightY) =>
        [leftX, leftY, rightX, rightY, 60f, 70f, 45f, 90f, 75f, 90f];
}

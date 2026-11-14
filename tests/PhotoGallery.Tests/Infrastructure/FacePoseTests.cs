using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Pose from the detector's five points: eyes, nose, mouth corners.
/// </summary>
public sealed class FacePoseTests
{
    [Fact]
    public void Turn_OfAFaceLookingStraightAtTheCameraIsZero() =>
        Assert.Equal(0d, FacePose.Turn(Points(40, 50, 80, 50, 60, 70)), 6);

    [Fact]
    public void Turn_WithTheNoseInLineWithAnEyeIsOne() =>
        Assert.Equal(1d, FacePose.Turn(Points(40, 50, 80, 50, 80, 70)), 6);

    [Fact]
    public void Turn_IsTheSameWhicheverWayTheFaceIsTurned() =>
        Assert.Equal(
            FacePose.Turn(Points(40, 50, 80, 50, 50, 70)),
            FacePose.Turn(Points(40, 50, 80, 50, 70, 70)),
            6);

    [Fact]
    public void Turn_DoesNotMistakeAHeadOnItsSideForATurnedOne()
    {
        // A baby asleep on its side: the eye line is vertical and the nose sits
        // beside its midpoint, exactly as a level face looking at the camera.
        Assert.Equal(0d, FacePose.Turn(Points(50, 40, 50, 80, 30, 60)), 6);
    }

    [Fact]
    public void Turn_WhenTheEyesAreInOnePlaceIsNotANumber() =>
        Assert.True(double.IsNaN(FacePose.Turn(Points(40, 50, 40, 50, 60, 70))));

    [Fact]
    public void Turn_RefusesAnythingButFivePoints() =>
        Assert.Throws<ArgumentException>(() => FacePose.Turn(new float[8]));

    [Fact]
    public void Roll_OfALevelFaceIsZero() =>
        Assert.Equal(0f, FacePose.Roll(Points(40, 50, 80, 50, 60, 70)), 6);

    [Fact]
    public void Roll_OfAFaceOnItsSideIsAQuarterTurn() =>
        Assert.Equal(MathF.PI / 2f, FacePose.Roll(Points(50, 40, 50, 80, 30, 60)), 5);

    [Fact]
    public void Roll_WhenTheEyesAreInOnePlaceIsNotANumber() =>
        Assert.True(float.IsNaN(FacePose.Roll(Points(40, 50, 40, 50, 60, 70))));

    private static float[] Points(
        float leftX, float leftY, float rightX, float rightY, float noseX, float noseY) =>
        [leftX, leftY, rightX, rightY, noseX, noseY, leftX + 5, noseY + 20, rightX - 5, noseY + 20];
}

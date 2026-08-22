using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// Straightening a photograph must not cost the names on it, so the boxes move
/// with the picture rather than being found again.
/// </summary>
public sealed class FaceBoundsTests
{
    // A 100x60 picture with a face in the top-left quarter, deliberately not
    // square and not centred so a transposed or mirrored answer cannot pass.
    private const int Width = 100;
    private const int Height = 60;

    private static readonly FaceBounds Face = new(10, 5, 20, 30);

    [Fact]
    public void TurnedClockwise_ByNothingLeavesTheBoxAlone() =>
        Assert.Equal(Face, Face.TurnedClockwise(Width, Height, 0));

    [Fact]
    public void TurnedClockwise_ByAQuarterSwapsTheSides()
    {
        FaceBounds turned = Face.TurnedClockwise(Width, Height, 90);

        // The picture is now 60 wide and 100 tall. What was 5 down the left edge
        // is now 5 in from the right, and what was 10 across is 10 down.
        Assert.Equal(new FaceBounds(Height - 5 - 30, 10, 30, 20), turned);
        Assert.Equal(Face.Area, turned.Area);
    }

    [Fact]
    public void TurnedClockwise_ByAHalfKeepsTheShape()
    {
        FaceBounds turned = Face.TurnedClockwise(Width, Height, 180);

        Assert.Equal(new FaceBounds(100 - 10 - 20, 60 - 5 - 30, 20, 30), turned);
    }

    [Fact]
    public void TurnedClockwise_FourTimesComesBackToWhereItStarted()
    {
        // The property that matters: rotating a photograph round is free, and
        // nothing drifts. Each quarter turn swaps the picture's sides, so the
        // sides have to be swapped with it.
        FaceBounds moving = Face;
        (int w, int h) = (Width, Height);

        for (int turn = 0; turn < 4; turn++)
        {
            moving = moving.TurnedClockwise(w, h, 90);
            (w, h) = (h, w);
        }

        Assert.Equal(Face, moving);
    }

    [Fact]
    public void TurnedClockwise_ThreeQuartersIsTheSameAsOneTheOtherWay()
    {
        FaceBounds right = Face.TurnedClockwise(Width, Height, 270);
        FaceBounds left = Face.TurnedClockwise(Width, Height, -90);

        Assert.Equal(right, left);
    }

    [Fact]
    public void TurnedClockwise_KeepsTheBoxInsideThePicture()
    {
        // A box that ran off the edge would be cut to nothing by the crop and
        // show as a grey square, which is how a wrong sign here would look.
        foreach (int degrees in new[] { 90, 180, 270 })
        {
            FaceBounds turned = Face.TurnedClockwise(Width, Height, degrees);
            (int w, int h) = degrees == 180 ? (Width, Height) : (Height, Width);

            Assert.True(turned.X >= 0 && turned.Y >= 0, $"{degrees}: negative corner");
            Assert.True(turned.X + turned.Width <= w, $"{degrees}: past the right edge");
            Assert.True(turned.Y + turned.Height <= h, $"{degrees}: past the bottom edge");
        }
    }
}

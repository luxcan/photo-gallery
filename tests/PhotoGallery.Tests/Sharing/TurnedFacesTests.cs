using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Why a face can be keyed on its box alone, once the turns have settled.
/// </summary>
/// <remarks>
/// A box means nothing on its own. Turning a photograph rewrites every face's
/// bounds, so confirming a name and then straightening the picture leaves the
/// box that was X sitting at Y, while the machine that never turned it still
/// holds X.
///
/// <para>The first draft answered that by carrying the rotation inside every
/// face key and normalising before matching. It is not needed, and this is the
/// arithmetic that makes the simpler answer safe: once both machines have agreed
/// the turn and each has moved its own boxes through the same pure function over
/// the same pre-turn dimensions, they are in the same frame. An ordering rule
/// instead of a wider key - and an ordering rule rests on two turns that reach
/// the same place actually reaching it.</para>
/// </remarks>
public sealed class TurnedFacesTests
{
    private const int Width = 1000;
    private const int Height = 800;

    private static readonly FaceBounds Head = new(120, 90, 60, 40);

    [Fact]
    public void TwoQuarterTurnsLandWhereOneHalfTurnDoes()
    {
        // The claim the whole ordering rule rests on. Two machines that reached
        // the same rotation by different routes must hold the same boxes, or one
        // of them holds answers the other cannot match.
        FaceBounds quarter = Head.TurnedClockwise(Width, Height, 90);
        FaceBounds twice = quarter.TurnedClockwise(Height, Width, 90);

        Assert.Equal(Head.TurnedClockwise(Width, Height, 180), twice);
    }

    [Fact]
    public void ThreeQuarterTurnsLandWhereOneTheOtherWayDoes()
    {
        FaceBounds a = Head.TurnedClockwise(Width, Height, 90);
        FaceBounds b = a.TurnedClockwise(Height, Width, 90);
        FaceBounds c = b.TurnedClockwise(Width, Height, 90);

        Assert.Equal(Head.TurnedClockwise(Width, Height, 270), c);
    }

    [Fact]
    public void ANameLandsOnTheSameFaceOnceBothMachinesHaveTurnedThePicture()
    {
        // Mum names the face, then straightens the photograph, so what she
        // publishes names the turned box.
        FaceBounds turned = Head.TurnedClockwise(Width, Height, 90);
        AssetKey photo = Pictures.Photo(@"2019\sideways.jpg");
        FaceKey published = new(photo, turned);

        Guid ana = Guid.NewGuid();
        Machine mum = new Machine("Mum").Turns(photo, 90, Monday).Confirms(published, ana, Monday);

        // Dad has not turned it yet, so his box is still where the detector left
        // it. Nothing matches, and the answer waits rather than being lost.
        MergePlan before = DecisionMerge.Merge(
            new Machine("Dad").Set(),
            [mum.Set()],
            Holding(photo, Head),
            Now);

        Assert.Empty(before.Answers);
        Assert.Equal(90, Assert.Single(before.Turns).Rotation);
        Assert.Single(before.Held.Answers);

        // Once the turn is applied his boxes move through the same arithmetic,
        // and the plain key matches exactly. This is the ordering rule: turns
        // first, then names.
        MergePlan after = DecisionMerge.Merge(
            new Machine("Dad").Turns(photo, 90, Monday).Set(),
            [mum.Set()],
            Holding(photo, turned),
            Now);

        Assert.Equal(published, Assert.Single(after.Answers).Face);
        Assert.Empty(after.Held.Answers);
    }

    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Monday.AddDays(30);

    private static LibraryContents Holding(AssetKey photo, FaceBounds face) =>
        new(
            new HashSet<Guid> { Machine.Share },
            new HashSet<AssetKey> { photo },
            new Dictionary<AssetKey, IReadOnlyList<FaceBounds>> { [photo] = [face] });
}

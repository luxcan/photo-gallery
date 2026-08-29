using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A photograph that goes away and comes back, on the machines that did not
/// send it away.
/// </summary>
/// <remarks>
/// The asymmetry this closes: setting a duplicate aside moves the file off the
/// shared drive, and the machine that did it keeps its row deliberately - that
/// row is the only thing that knows how to put the file back. Every other
/// machine's scan sees the file simply vanish and removes it, so a restore later
/// brings the picture back to three laptops that have never named it. The
/// one-way door the app went to trouble to prevent had only moved.
/// </remarks>
public sealed class QuarantineTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Dad => _house.Dad;

    [Fact]
    public async Task APhotographThatGoesAwayLeavesItsNamesWaitingForIt()
    {
        Named();

        // The scan finds the file gone and removes the row, as it does for a
        // deletion and for a quarantine on somebody else's machine alike.
        await VanishesAsync();

        Assert.Empty(Dad.Db.Assets);
        Assert.Empty(Dad.Db.FaceAssignments);

        HeldDecision waiting = Assert.Single(Dad.Db.HeldDecisions);
        Assert.Equal(@"2019\a.jpg", waiting.RelativePath);
        Assert.Equal(HeldDecisionKind.FaceAnswer, waiting.Kind);
    }

    [Fact]
    public async Task AndTheNamesComeBackWhenThePhotographDoes()
    {
        Named();
        await VanishesAsync();

        // Restored: the file is back on the share, and this machine's next scan
        // indexes it as new. Nobody here ever named it.
        Dad.Face(Dad.Photo(@"2019\a.jpg"), Head);

        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Equal(1, swept.Applied);

        FaceAssignment back = Assert.Single(Dad.Db.FaceAssignments);
        Assert.Equal(AssignmentSource.Confirmed, back.Source);

        // With the moment and the machine it was decided on, not this scan's.
        Assert.Equal(Monday, back.DecidedUtc);
    }

    [Fact]
    public async Task AnAlbumAPhotographWasInComesBackWithIt()
    {
        // Not only names. Everything keyed on the photograph is parked, which is
        // what makes a folder moved and moved back cost nothing.
        Asset photo = Dad.Photo(@"2019\a.jpg");
        Collection album = Dad.Album("Bali", Monday);
        Dad.Db.CollectionMembers.Add(new CollectionMember
        {
            CollectionId = album.Id,
            AssetId = photo.Id,
            AddedUtc = Monday,
        });
        Dad.Db.SaveChanges();

        await VanishesAsync();

        HeldDecision waiting = Assert.Single(Dad.Db.HeldDecisions);
        Assert.Equal(HeldDecisionKind.AlbumMembership, waiting.Kind);
    }

    [Fact]
    public async Task APhotographNobodyDecidedAnythingAboutLeavesNothingBehind()
    {
        // Most of a library is this. A row per vanished photograph would grow
        // the table with the size of the library rather than with what anybody
        // had said.
        Dad.Photo(@"2019\a.jpg");

        await VanishesAsync();

        Assert.Empty(Dad.Db.HeldDecisions);
    }

    [Fact]
    public async Task ParkingTwiceKeepsOneAnswer()
    {
        Named();
        await VanishesAsync();

        // The same photograph coming and going again, which is what a drive that
        // reconnects intermittently looks like.
        Dad.Face(Dad.Photo(@"2019\a.jpg"), Head);
        await Dad.Waiting.HandleAsync();
        await VanishesAsync();

        Assert.Single(Dad.Db.HeldDecisions);
    }

    // ------------------------------------------------------------------ setup

    /// <summary>A photograph with somebody's name on it.</summary>
    private void Named() =>
        Dad.Answer(
            Dad.Face(Dad.Photo(@"2019\a.jpg"), Head),
            Dad.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

    /// <summary>
    /// The file is no longer where the source says it is, and the scan reaches
    /// the only conclusion it can.
    /// </summary>
    /// <remarks>
    /// Driven through the scanner's own removal rather than by deleting rows,
    /// because the claim is about what a scan does - and a test that deleted the
    /// rows itself would assert the parking that it had just arranged.
    /// </remarks>
    private async Task VanishesAsync() => await Dad.ScanFindsNothingAsync();

    public void Dispose() => _house.Dispose();
}

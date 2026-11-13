using Microsoft.EntityFrameworkCore;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A photograph turned on another machine, where the file itself took the turn.
/// </summary>
/// <remarks>
/// Telling the file writes its orientation tag and puts its dates back, so no
/// scan here notices, and the turn that travels records no rotation of its own.
/// Before this was handled, a machine that had prepared the photograph earlier
/// kept it on its side however often it synced.
/// </remarks>
public sealed class FileTurnsTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Head = new(120, 90, 60, 40);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task ThePicturesHereTurnByAsMuchAsTheFileWasTurned()
    {
        TurnedByMumInTheFile();
        Asset photo = Dad.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Face(photo, Head);
        Dad.Renditions.Behind("aa11.jpg", 270);

        await SyncAsync();

        Assert.Equal(("aa11.jpg", 270), Assert.Single(Dad.Renditions.Turns));

        Asset row = Dad.Db.Assets.AsNoTracking().Single();
        Assert.Equal(0, row.Rotation);
        Assert.Equal(Monday, row.RotatedUtc);

        // The boxes move with the picture, so the names Mum gives the faces on
        // her turned copy land on the same faces here.
        Assert.Equal(
            Head.TurnedClockwise(1024, 768, 270),
            Dad.Db.Faces.AsNoTracking().Single().Bounds);
    }

    [Fact]
    public async Task OnceCaughtUp_TheNextSyncAsksNothing()
    {
        TurnedByMumInTheFile();
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Renditions.Behind("aa11.jpg", 90);

        await SyncAsync();
        Dad.Renditions.Behind("aa11.jpg", 0);
        await SyncAsync();

        Assert.Single(Dad.Renditions.Turns);
        Assert.True(await Dad.NothingPlannedAsync(), "a turn already taken plans nothing");
    }

    [Fact]
    public async Task PicturesThatAlreadyMatchTheFileAreLeftAlone()
    {
        TurnedByMumInTheFile();
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");

        await SyncAsync();

        Assert.Empty(Dad.Renditions.Turns);
        Assert.Equal(Monday, Dad.Db.Assets.AsNoTracking().Single().RotatedUtc);
    }

    [Fact]
    public async Task AnOriginalThatCannotBeRead_KeepsTheTurnWaiting()
    {
        TurnedByMumInTheFile();
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Renditions.Behind("aa11.jpg", null);

        await SyncAsync();

        Assert.Empty(Dad.Renditions.Turns);
        Assert.Null(Dad.Db.Assets.AsNoTracking().Single().RotatedUtc);
        Assert.Single(Dad.Db.HeldDecisions, held => held.Kind == HeldDecisionKind.Turn);
    }

    /// <summary>
    /// What Mum's library holds after turning a photograph whose file took the
    /// tag: the moment, and no rotation of the app's own.
    /// </summary>
    private void TurnedByMumInTheFile()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");

        Asset turned = Mum.Db.Assets.Single();
        turned.Rotation = 0;
        turned.RotatedUtc = Monday;
        turned.RotatedBy = Mum.MachineId;
        Mum.Db.SaveChanges();
        Mum.Db.ChangeTracker.Clear();
    }

    private async Task SyncAsync()
    {
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        Dad.Db.ChangeTracker.Clear();
    }

    public void Dispose() => _house.Dispose();
}

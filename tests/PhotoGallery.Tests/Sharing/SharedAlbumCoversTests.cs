using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// The picture an album shows for itself, crossing between two machines.
/// </summary>
/// <remarks>
/// An album arriving from another laptop was created with no cover and then
/// filled with photographs by a path that never worked one out, so it stood on
/// the wall as a grey square - and no later pass repaired it, because the
/// rebuild only ever touches the albums it proposed itself. That is the first
/// half of what is argued out here.
///
/// <para>The second half is whose picture it is. A cover the app worked out is a
/// guess and stays at home; a cover somebody chose is an answer and travels,
/// settled on its own date the way a shelf is - because renaming an album and
/// choosing its picture are two decisions, and one date for both hands every
/// argument about pictures to whoever typed a name last.</para>
/// </remarks>
public sealed class SharedAlbumCoversTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tuesday = new(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    /// <summary>
    /// The bug as it was reported: her album arrives, and shows nothing.
    /// </summary>
    [Fact]
    public async Task AnAlbumThatArrivesHasAPictureToShowForItself()
    {
        Album bali = Mum.Album("Bali", Monday);
        Mum.Put(bali, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Album landed = Assert.Single(Dad.Db.Albums);
        Assert.NotEqual(0, landed.CoverAssetId);

        // Asserted the way the wall reads it as well as on the row, because a
        // cover pointing at a row with no rendition is still a grey square.
        AlbumSummary shown = Assert.Single(await new SqliteAlbumRepository(Dad.Db).GetAsync());
        Assert.Equal("aaa.jpg", shown.CoverThumbnailName);
    }

    /// <summary>And having settled it once, there is nothing left to settle.</summary>
    [Fact]
    public async Task MergingTwiceLeavesNothingLeftToSettle()
    {
        Album bali = Mum.Album("Bali", Monday);
        Mum.Put(bali, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);
        await Mum.Albums.SetCoverAsync(bali.Id, Mum.Db.Assets.Single().Id);

        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.True(await PlanIsEmptyAsync(Dad), "the merge plan should have nothing left in it");
    }

    /// <summary>
    /// The picture somebody picked is the picture both machines show.
    /// </summary>
    /// <remarks>
    /// Chosen against the rule on purpose. The rule takes the photograph with
    /// the most faces in it, so choosing the one with none proves the choice
    /// crossed rather than that two machines happened to guess alike.
    /// </remarks>
    [Fact]
    public async Task ThePictureSomebodyChoseIsTheOneTheOtherMachineShows()
    {
        Asset plain = Mum.Prepared(@"2019\plain.jpg", "plain.jpg");
        Asset crowd = Mum.Prepared(@"2019\crowd.jpg", "crowd.jpg");
        Mum.Face(crowd, Head);

        Album bali = Mum.Album("Bali", Monday);
        Mum.Put(bali, plain, Monday);
        Mum.Put(bali, crowd, Monday);

        Assert.True(await Mum.Albums.SetCoverAsync(bali.Id, plain.Id));

        Asset there = Dad.Prepared(@"2019\plain.jpg", "plain.jpg");
        Dad.Face(Dad.Prepared(@"2019\crowd.jpg", "crowd.jpg"), Head);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Album landed = Assert.Single(Dad.Db.Albums);
        Assert.Equal(there.Id, landed.CoverAssetId);

        // And it is recorded as a decision, or his own next photograph would
        // replace it with the rule's answer and say nothing.
        Assert.NotNull(landed.CoverChosenUtc);
    }

    /// <summary>
    /// A cover nobody chose stays at home, and each machine works out its own.
    /// </summary>
    /// <remarks>
    /// The rule that keeps this from being a race. Both libraries derive a cover
    /// from the same photographs; sending one as though it were an answer would
    /// put this library's guess up against the other's equally good one, and
    /// beat it again on every merge afterwards.
    ///
    /// <para>The two machines are made to guess differently on purpose - the
    /// face pass has found somebody in a different photograph on each - because
    /// that is the only arrangement in which "his own guess won" and "her guess
    /// crossed" are different answers.</para>
    /// </remarks>
    [Fact]
    public async Task TheAppsOwnGuessDoesNotTravel()
    {
        Asset hers = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Asset his = Mum.Prepared(@"2019\b.jpg", "bbb.jpg");
        Mum.Face(hers, Head);

        Album bali = Mum.Album("Bali", Monday);
        Mum.Put(bali, hers, Monday);
        Mum.Put(bali, his, Monday);

        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");
        Asset mine = Dad.Prepared(@"2019\b.jpg", "bbb.jpg");
        Dad.Face(mine, Head);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Album landed = Assert.Single(Dad.Db.Albums);
        Assert.Equal(mine.Id, landed.CoverAssetId);
        Assert.Null(landed.CoverChosenUtc);
    }

    /// <summary>
    /// The later choice wins, the way the later name does.
    /// </summary>
    [Fact]
    public async Task ThePictureChosenLastIsTheOneBothMachinesKeep()
    {
        Guid album = Guid.NewGuid();

        Album hers = Mum.Album("Bali", Monday, album);
        Asset herPhoto = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Mum.Put(hers, herPhoto, Monday);
        await Mum.Albums.SetCoverAsync(hers.Id, herPhoto.Id);

        Album his = Dad.Album("Bali", Monday, album);
        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");
        Asset hisPhoto = Dad.Prepared(@"2019\b.jpg", "bbb.jpg");
        Dad.Put(his, hisPhoto, Monday);
        await Dad.Albums.SetCoverAsync(his.Id, hisPhoto.Id);

        // His is the later choice, because SetCoverAsync stamps the moment it
        // runs and his ran second.
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Equal(hisPhoto.Id, Dad.Db.Albums.Single(row => row.PublicId == album).CoverAssetId);
    }

    /// <summary>
    /// A cover about a photograph this machine has not indexed waits, and costs
    /// nothing while it waits.
    /// </summary>
    /// <remarks>
    /// Not held in a row of its own, unlike a name or a membership: a decision
    /// set is whole state rather than a log, so the machine that chose it goes
    /// on saying so and the choice lands by itself on the first merge after the
    /// scan. Meanwhile the album shows the picture this library's own rule
    /// chooses rather than nothing at all.
    ///
    /// <para>That it does not sit in every plan while it waits is asserted where
    /// it can be asserted exactly, on the plan itself - see
    /// <c>DecisionMergeTests.ACoverForAPhotographThisLibraryHasNotGotIsNotProposedYet</c>.
    /// Here the membership for that same photograph is waiting too, so a plan
    /// with something left in it would say nothing about covers.</para>
    /// </remarks>
    [Fact]
    public async Task ACoverAboutAPhotographThisMachineHasNotGotLandsWhenItArrives()
    {
        Album bali = Mum.Album("Bali", Monday, Guid.NewGuid());
        Asset shared = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Asset only = Mum.Prepared(@"2019\hers.jpg", "hers.jpg");
        Mum.Put(bali, shared, Monday);
        Mum.Put(bali, only, Monday);
        await Mum.Albums.SetCoverAsync(bali.Id, only.Id);

        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Album landed = Dad.Db.Albums.Single();
        Assert.NotEqual(0, landed.CoverAssetId);
        Assert.Null(landed.CoverChosenUtc);

        // And when the scan finds her photograph, the choice she made lands.
        Asset arrived = Dad.Prepared(@"2019\hers.jpg", "hers.jpg");
        await Dad.Merging.HandleAsync();

        Album settled = Dad.Db.Albums.Single();
        Assert.Equal(arrived.Id, settled.CoverAssetId);
        Assert.NotNull(settled.CoverChosenUtc);
    }

    /// <summary>
    /// The same is true of an album filled by answers that were waiting.
    /// </summary>
    /// <remarks>
    /// The common case on a second machine: the merge happens first and the scan
    /// afterwards, so every membership is parked and the album is empty until
    /// the sweep that applies them. That sweep writes memberships through the
    /// same path a merge does, and it has to leave a cover behind just as a
    /// merge does.
    /// </remarks>
    [Fact]
    public async Task AnAlbumFilledByAnswersThatWereWaitingGetsAPictureToo()
    {
        Album bali = Mum.Album("Bali", Monday);
        Mum.Put(bali, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        await Mum.Publishing.HandleAsync();

        MergeResult merged = await Dad.Merging.HandleAsync();
        Assert.Equal(1, merged.Outcome.Held);
        Assert.Equal(0, Dad.Db.Albums.Single().CoverAssetId);

        Asset arrived = Dad.Prepared(@"2019\a.jpg", "aaa.jpg");
        await Dad.Waiting.HandleAsync();

        Assert.Equal(arrived.Id, Dad.Db.Albums.Single().CoverAssetId);
    }

    /// <summary>
    /// An album a merge takes a photograph out of stops showing it.
    /// </summary>
    /// <remarks>
    /// A photograph belongs to one album, so a merge that moves one takes it out
    /// of whatever this library had it in - and that album may have been showing
    /// exactly that picture. Locally the same move is put right on the way past;
    /// through a merge nothing did it.
    /// </remarks>
    [Fact]
    public async Task AnAlbumAMergeEmptiesStopsShowingThePhotographItLost()
    {
        Asset photo = Dad.Prepared(@"2019\a.jpg", "aaa.jpg");
        Album his = Dad.Album("Weekend", Monday);
        Dad.Put(his, photo, Monday);
        await Dad.Albums.SetCoverAsync(his.Id, photo.Id);

        Album hers = Mum.Album("Bali", Tuesday);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Tuesday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Album emptied = Dad.Db.Albums.Single(row => row.PublicId == his.PublicId);
        Assert.Empty(Dad.Db.AlbumMembers.Where(member => member.AlbumId == emptied.Id));
        Assert.Equal(0, emptied.CoverAssetId);
        Assert.Null(emptied.CoverChosenUtc);
    }

    /// <summary>What one more merge would still find to do, if anything.</summary>
    private static async Task<bool> PlanIsEmptyAsync(Library library)
    {
        MergeResult again = await library.Merging.HandleAsync();
        return again.Outcome.ChangedNothing;
    }

    public void Dispose() => _house.Dispose();
}

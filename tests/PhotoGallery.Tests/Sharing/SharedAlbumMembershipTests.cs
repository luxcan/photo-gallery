using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Which album a photograph lands in, when two machines name albums differently.
/// </summary>
/// <remarks>
/// A membership names its album by an identity, and only an album somebody made
/// has one that means the same thing on both machines. A proposal is derived -
/// the pass deletes and reinserts the row - so each library mints its own, and
/// the run of days is all the two can agree on.
///
/// <para>The write knew none of that. It looked the destination up by the
/// publishing machine's identity, missed, and dropped the membership with no
/// row, no waiting answer and no count - then planned it again on the next
/// merge, and the one after. Silent on the way in, and never finished.</para>
/// </remarks>
public sealed class SharedAlbumMembershipTests : IDisposable
{
    private const string Days = "2019-03-03..2019-03-05";

    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tuesday = new(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    /// <summary>
    /// Photographs put into a suggestion both machines kept arrive.
    /// </summary>
    /// <remarks>
    /// The case the write could never do. Both rows carry the same run of days
    /// and two different identities, which is what a rebuild leaves behind on
    /// any two libraries holding the same photographs.
    /// </remarks>
    [Fact]
    public async Task PhotographsPutIntoASuggestionBothMachinesKeptArrive()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true);
        Asset photo = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Mum.Put(hers, photo, Monday);

        Album his = Dad.Suggestion("March 2019", Days, kept: true);
        Asset here = Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        Assert.NotEqual(hers.PublicId, his.PublicId);

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Dad.Merging.HandleAsync();

        AlbumMember landed = Assert.Single(Dad.Db.AlbumMembers);
        Assert.Equal(here.Id, landed.AssetId);
        Assert.Equal(his.Id, landed.AlbumId);
        Assert.Equal(1, merged.Outcome.PhotographsMoved);

        Assert.True(await NothingLeftAsync(Dad), "the second merge should find nothing to do");
    }

    /// <summary>
    /// And they arrive just as well when the photograph turns up later.
    /// </summary>
    /// <remarks>
    /// The order of operations must not matter - scan first or share first, the
    /// answer lands either way - so a membership about a photograph this library
    /// has not indexed waits for it. What comes back to settle it arrives on its
    /// own, without the machine's albums that carried it, so the moment it is
    /// parked is the last one at which the album can be worked out. Parked under
    /// the other machine's name it would fail to place on the very sweep that
    /// exists to make it land.
    /// </remarks>
    [Fact]
    public async Task APhotographThatArrivesLaterStillJoinsTheSuggestionBothKept()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        Album his = Dad.Suggestion("March 2019", Days, kept: true);

        await Mum.Publishing.HandleAsync();

        // Nothing of his to put it on yet, so it waits.
        MergeResult merged = await Dad.Merging.HandleAsync();
        Assert.Equal(1, merged.Outcome.Held);
        Assert.Empty(Dad.Db.AlbumMembers);

        Asset arrived = Dad.Prepared(@"2019\a.jpg", "aaa.jpg");
        await Dad.Waiting.HandleAsync();

        AlbumMember landed = Assert.Single(Dad.Db.AlbumMembers);
        Assert.Equal(arrived.Id, landed.AssetId);
        Assert.Equal(his.Id, landed.AlbumId);
    }

    /// <summary>
    /// Nothing joins an album this library has thrown away, and it stops asking.
    /// </summary>
    /// <remarks>
    /// The plan is asserted rather than the write, because the two used to
    /// disagree and that disagreement was the bug: one move planned, none
    /// applied, "Nothing new" on screen, and the same move planned again on
    /// every merge for ever.
    /// </remarks>
    [Fact]
    public async Task NothingJoinsAnAlbumThisLibraryHasThrownAway()
    {
        Album hers = Mum.Album("Genting", Monday);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        Album his = Dad.Album("Genting", Monday, hers.PublicId);
        Dad.Put(his, Dad.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);
        Dad.Remove(his, Tuesday);

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.Empty(merged.Outcome.Moves);
        Assert.Empty(Dad.Db.AlbumMembers);
        Assert.True(merged.Outcome.ChangedNothing);

        Assert.True(await NothingLeftAsync(Dad), "a move that cannot land must not be re-planned");
    }

    /// <summary>
    /// Nor an album this library never had, which somebody else has thrown away.
    /// </summary>
    /// <remarks>
    /// Three machines, because two cannot express it: a library stops publishing
    /// an album's memberships the moment it deletes the album, so the tombstone
    /// and the memberships have to come from different machines while the one
    /// receiving them has neither.
    ///
    /// <para>The tombstone used to be discarded here - nothing referred to the
    /// album, so there seemed to be nothing to remember - and that left this
    /// library unable to tell "deleted" from "never heard of". It planned the
    /// same moves on every merge, applied none of them, and said "Nothing new"
    /// every time.</para>
    /// </remarks>
    [Fact]
    public async Task NothingJoinsAnAlbumSomebodyElseThrewAwayBeforeThisOneHeardOfIt()
    {
        Library gran = _house.Add("Gran");

        Guid shared = Guid.NewGuid();

        Album hers = Mum.Album("Genting", Monday, shared);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        // Gran had the same album and threw it away, which is the fact Dad is
        // missing.
        gran.Remove(gran.Album("Genting", Monday, shared), Tuesday);

        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        await Mum.Publishing.HandleAsync();
        await gran.Publishing.HandleAsync();

        await Dad.Merging.HandleAsync();

        Assert.Empty(Dad.Db.AlbumMembers);
        Assert.Empty(Dad.Db.Albums);

        // Remembered as gone rather than forgotten, which is what lets the next
        // merge settle instead of asking again.
        Assert.NotNull(Dad.Db.Albums.IgnoreQueryFilters().Single().DeletedUtc);
        Assert.True(await NothingLeftAsync(Dad), "the house must converge, not ask for ever");
    }

    /// <summary>
    /// The same when the album somebody threw away was a kept suggestion.
    /// </summary>
    /// <remarks>
    /// The branch the first fix carved out. A tombstone is written for an album
    /// this library never held, but not for one derived from a run of days - its
    /// key is what a rebuild matches on, and a tombstone carrying one would sit
    /// in the way of the days this machine is about to group again. With no row
    /// to find, "never heard of it" and "gone" looked alike again, and the moves
    /// came back on every merge. The plan answers it instead: an album arriving
    /// already thrown away is one nobody will write, so nothing is promised a
    /// row that is never made.
    /// </remarks>
    [Fact]
    public async Task NothingJoinsAKeptSuggestionSomebodyElseThrewAway()
    {
        Library gran = _house.Add("Gran");

        Album hers = Mum.Suggestion("March 2019", Days, kept: true);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        gran.Remove(gran.Suggestion("March 2019", Days, kept: true), Tuesday);

        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        await Mum.Publishing.HandleAsync();
        await gran.Publishing.HandleAsync();

        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.Empty(merged.Outcome.Moves);
        Assert.Empty(Dad.Db.AlbumMembers);
        Assert.True(await NothingLeftAsync(Dad), "the house must converge, not ask for ever");
    }

    /// <summary>
    /// A suggestion this library has not kept takes nothing, and says so by
    /// leaving the plan empty.
    /// </summary>
    /// <remarks>
    /// Its contents are derived here, and the next rebuild owns them - it prunes
    /// whatever its own clustering no longer claims. Writing another machine's
    /// answers into it would be a write that a scan may quietly undo, so the
    /// merge refuses instead.
    ///
    /// <para>The photographs do not arrive, which is the honest answer and not
    /// yet the right one: what is missing is that <em>keeping</em> a suggestion
    /// does not itself travel, so the two machines never agree that this album
    /// is anybody's.</para>
    /// </remarks>
    [Fact]
    public async Task ASuggestionThisLibraryHasNotKeptTakesNothingAndStopsAsking()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        Dad.Suggestion("March 2019", Days, kept: false);
        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.Empty(merged.Outcome.Moves);
        Assert.Empty(Dad.Db.AlbumMembers);

        Assert.True(await NothingLeftAsync(Dad), "a refusal must be settled, not repeated");
    }

    /// <summary>What one more merge would still find to do, if anything.</summary>
    /// <remarks>
    /// Asked of the plan rather than the write counter. A counter of zero only
    /// says nothing was written; a plan still full says the two libraries have
    /// not actually agreed, and every merge from now until somebody notices will
    /// try to settle the same photograph again.
    /// </remarks>
    private static async Task<bool> NothingLeftAsync(Library library)
    {
        MergeResult again = await library.Merging.HandleAsync();
        return again.Outcome.Moves.Count == 0 && again.Outcome.ChangedNothing;
    }

    public void Dispose() => _house.Dispose();
}

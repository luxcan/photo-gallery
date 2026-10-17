using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
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
    /// The branch the first fix carved out, when a tombstone was written for an
    /// album this library never held but not for one derived from a run of days.
    /// With no row to find, "never heard of it" and "gone" looked alike again,
    /// and the moves came back on every merge. The plan answers it on its own:
    /// an album arriving already thrown away is one nobody will put back, so
    /// nothing is promised a row that is never made. The tombstone is written
    /// now as well, for the rebuild's sake rather than the plan's - see
    /// <see cref="AKeptSuggestionSomebodyDeletedIsNotSuggestedWhereItHadNotBeenGroupedYet"/>.
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
    /// A kept suggestion somebody deleted is not suggested again on a machine
    /// that was still only suggesting it.
    /// </summary>
    /// <remarks>
    /// Deleting a kept suggestion means "never suggest those days again", and
    /// the tombstone travels on its run of days. Here it reaches Dad's own
    /// suggestion for the same days, turns it into a tombstone of his own and
    /// lets its photographs go - and those photographs group into exactly the
    /// same days on his next scan. The rebuild could not see a deleted row, so
    /// it added a new one under a key the tombstone still held, the database
    /// refused it, and every scan on Dad's laptop failed from then on.
    /// </remarks>
    [Fact]
    public async Task AKeptSuggestionSomebodyDeletedIsNotSuggestedAgainWhereItWasOnlySuggested()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true);
        await Mum.Albums.DeleteAsync(hers.Id);

        Album his = Dad.Suggestion("March 2019", Days, kept: false);
        Asset a = Dad.Prepared(@"2019\a.jpg", "aaa.jpg");
        Asset b = Dad.Prepared(@"2019\b.jpg", "bbb.jpg");
        Dad.Put(his, a, Monday);
        Dad.Put(his, b, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        Dad.Db.ChangeTracker.Clear();

        Assert.Empty(Dad.Db.Albums);
        Assert.Empty(Dad.Db.AlbumMembers);

        await Dad.Albums.SaveProposalsAsync([Proposal(a, b)]);

        Assert.Empty(Dad.Db.Albums);
        Assert.Empty(Dad.Db.AlbumMembers);
        Assert.True(await NothingLeftAsync(Dad), "the house must converge, not ask for ever");

        // And on the scan after the next merge, so the answer is shown to
        // outlast both.
        await Dad.Albums.SaveProposalsAsync([Proposal(a, b)]);

        Assert.Empty(Dad.Db.Albums);
    }

    /// <summary>
    /// The same when the deletion arrives before this library has grouped
    /// those days at all.
    /// </summary>
    /// <remarks>
    /// A tombstone for an album this library never held used to be written
    /// only when it had no run of days, because a row carrying one would stand
    /// in the way of the days this machine was about to group. Standing in
    /// that way is now exactly what it is for. Without the row, Dad's next scan
    /// suggested the days somebody had already thrown away, and kept doing so
    /// until a merge caught up with it.
    /// </remarks>
    [Fact]
    public async Task AKeptSuggestionSomebodyDeletedIsNotSuggestedWhereItHadNotBeenGroupedYet()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true);
        await Mum.Albums.DeleteAsync(hers.Id);

        Asset a = Dad.Prepared(@"2019\a.jpg", "aaa.jpg");
        Asset b = Dad.Prepared(@"2019\b.jpg", "bbb.jpg");

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        await Dad.Albums.SaveProposalsAsync([Proposal(a, b)]);

        Assert.Empty(Dad.Db.Albums);
        Assert.Empty(Dad.Db.AlbumMembers);
        Assert.True(await NothingLeftAsync(Dad), "the house must converge, not ask for ever");
    }

    /// <summary>
    /// And on a third machine, when what reaches it is another machine's
    /// suggestion that the deletion turned into a tombstone.
    /// </summary>
    /// <remarks>
    /// Mum deletes her kept suggestion. Dad only ever had a suggestion for
    /// those days, and the merge turns it into a tombstone of his own - still a
    /// suggestion, under his identity and his name. Both tombstones reach Gran,
    /// who has not grouped those days yet, and which one she settles on follows
    /// the name: Dad's here, because he renamed his. The merge wrote a
    /// tombstone for days this library had no row for only when the album it
    /// settled on was not a suggestion, so Gran was left with no answer for
    /// those days and her next scan suggested the very days somebody had
    /// deleted. A suggestion that is a tombstone exists only because a kept one
    /// was deleted somewhere, so it is the same answer.
    /// </remarks>
    [Fact]
    public async Task AKeptSuggestionSomebodyDeletedIsNotSuggestedWhereItArrivesAsAnotherMachinesSuggestion()
    {
        Library gran = _house.Add("Gran");

        Album hers = Mum.Suggestion("March 2019", Days, kept: true);
        await Mum.Albums.DeleteAsync(hers.Id);

        // Renamed but never kept, so it is his name - and with it his row's
        // origin - that wins wherever the two tombstones meet.
        Album his = Dad.Suggestion("March 2019", Days, kept: false);
        await Dad.Albums.RenameAsync(his.Id, "Melaka");
        Dad.Db.ChangeTracker.Clear();

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        await Dad.Publishing.HandleAsync();

        Album thrown = Dad.Db.Albums.IgnoreQueryFilters().AsNoTracking().Single();
        Assert.Equal(AlbumOrigin.Proposed, thrown.Origin);
        Assert.NotNull(thrown.DeletedUtc);

        Asset a = gran.Prepared(@"2019\a.jpg", "aaa.jpg");
        Asset b = gran.Prepared(@"2019\b.jpg", "bbb.jpg");

        await gran.Merging.HandleAsync();
        await gran.Albums.SaveProposalsAsync([Proposal(a, b)]);

        Assert.Empty(gran.Db.Albums);
        Assert.Empty(gran.Db.AlbumMembers);
        Assert.True(await NothingLeftAsync(gran), "the house must converge, not ask for ever");
    }

    /// <summary>
    /// A photograph somebody filed stays where they filed it when another
    /// machine later finds it among the days of a suggestion both kept.
    /// </summary>
    /// <remarks>
    /// Dad files a photograph into an album of his own. Mum, who has not shared
    /// since, scans and finds it among the days of a suggestion they both kept.
    /// The rebuild used to put it into her kept album, dated the moment it ran -
    /// and an album somebody kept sends its photographs as decisions, so the
    /// app's grouping on her laptop beat Dad's filing on his, and the next merge
    /// took the photograph out of his album. A scan is not a decision, and must
    /// never outrank one.
    /// </remarks>
    [Fact]
    public async Task APhotographFiledOnOneMachineStaysFiledWhenAnotherScansItsDays()
    {
        Mum.Suggestion("March 2019", Days, kept: true);
        Asset mumsCopy = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");

        Dad.Suggestion("March 2019", Days, kept: true);
        Album best = Dad.Album("Best shots", Monday);
        Dad.Put(best, Dad.Prepared(@"2019\a.jpg", "aaa.jpg"), Tuesday);

        await Mum.Albums.SaveProposalsAsync([Proposal(mumsCopy)]);

        await Mum.Publishing.HandleAsync();
        await Dad.Publishing.HandleAsync();
        await Mum.Merging.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Equal(best.Id, Assert.Single(Dad.Db.AlbumMembers.AsNoTracking()).AlbumId);

        Album filed = Mum.Db.Albums.AsNoTracking().Single(album => album.PublicId == best.PublicId);
        Assert.Equal(filed.Id, Assert.Single(Mum.Db.AlbumMembers.AsNoTracking()).AlbumId);
    }

    /// <summary>
    /// What a scan offers for <see cref="Days"/>, holding these photographs.
    /// </summary>
    private static ProposedAlbum Proposal(params Asset[] photos) =>
        new(
            Days,
            "March 2019",
            new DateTime(2019, 3, 3, 10, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2019, 3, 5, 18, 0, 0, DateTimeKind.Unspecified),
            AlbumKind.Event,
            PlaceId: null,
            photos[0].Id,
            [.. photos.Select(photo => photo.Id)]);

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

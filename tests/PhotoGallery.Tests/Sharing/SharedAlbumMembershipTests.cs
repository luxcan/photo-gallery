using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Sharing;

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
    private static readonly DateTime Wednesday = new(2026, 3, 4, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Thursday = new(2026, 3, 5, 9, 0, 0, DateTimeKind.Utc);

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
    /// A suggestion kept on one laptop arrives kept on another that was only
    /// suggesting the same days, with its photographs, and a scan there does
    /// not take it back.
    /// </summary>
    /// <remarks>
    /// The common case: both laptops grouped the same days, so each has its own
    /// row for them. Keeping never travelled, so Dad's row stayed a suggestion -
    /// it refused Mum's photographs, published none of its own, and his next
    /// scan could remove it or rename it back. It is now kept where it stands,
    /// under his own identity, on the first merge.
    /// </remarks>
    [Fact]
    public async Task AKeptSuggestionArrivesKeptWithItsPhotographsAndSurvivesARebuild()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        Album his = Dad.Suggestion("March 2019", Days, kept: false);
        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        Dad.Db.ChangeTracker.Clear();

        Album adopted = Dad.Db.Albums.AsNoTracking().Single(album => album.Id == his.Id);
        Assert.Equal(AlbumOrigin.Accepted, adopted.Origin);
        Assert.Equal(Monday, adopted.KeptUtc);
        Assert.Equal(his.Id, Assert.Single(Dad.Db.AlbumMembers.AsNoTracking()).AlbumId);

        await Dad.Albums.SaveProposalsAsync([]);
        Assert.True(Dad.Db.Albums.AsNoTracking().Any(album => album.Id == his.Id));

        Assert.True(await Dad.NothingPlannedAsync(), "the next merge should find nothing to do");
    }

    /// <summary>
    /// A keep and a name typed to the same words settle in one merge.
    /// </summary>
    /// <remarks>
    /// Mum keeps the suggestion and types the very name Dad's laptop already
    /// shows. The name's date was written only when the name itself differed,
    /// so Dad's row never took it - and the album was planned again on every
    /// merge afterwards, for a change nobody could see.
    /// </remarks>
    [Fact]
    public async Task AKeepAndTheSameTypedNameSettleInOneMerge()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        await Mum.Albums.RenameAsync(hers.Id, "March 2019");
        Mum.Db.ChangeTracker.Clear();

        Album his = Dad.Suggestion("March 2019", Days, kept: false);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        Dad.Db.ChangeTracker.Clear();

        Album named = Mum.Db.Albums.AsNoTracking().Single(album => album.Id == hers.Id);
        Album adopted = Dad.Db.Albums.AsNoTracking().Single(album => album.Id == his.Id);

        Assert.NotNull(named.NamedUtc);
        Assert.Equal(named.NamedUtc, adopted.NamedUtc);
        Assert.Equal(AlbumOrigin.Accepted, adopted.Origin);
        Assert.True(await Dad.NothingPlannedAsync(), "the next merge should find nothing to do");
    }

    /// <summary>
    /// A photograph taken out of a kept suggestion before another laptop took
    /// the album in stays out on both.
    /// </summary>
    /// <remarks>
    /// Passed before keeping travelled, and has to go on passing. Dad's copy of
    /// those days still holds the photograph, put there by his own rebuild.
    /// Taking the album in as kept publishes everything in it as his, newer than
    /// anything Mum said - which would put the photograph straight back into the
    /// album she took it out of. Her refusal arrives in the same merge, so what
    /// his rebuild put there that somebody refused is let go.
    /// </remarks>
    [Fact]
    public async Task ARemovalMadeBeforeTheKeepStaysRemovedOnBothLaptops()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        Asset aMum = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Asset bMum = Mum.Prepared(@"2019\b.jpg", "bbb.jpg");
        Mum.Put(hers, aMum, Monday);
        Mum.Put(hers, bMum, Monday);
        await Mum.Albums.RemoveAsync(hers.Id, [bMum.Id]);
        Mum.Db.ChangeTracker.Clear();

        Album his = Dad.Suggestion("March 2019", Days, kept: false);
        Asset bDad = Dad.Prepared(@"2019\b.jpg", "bbb.jpg");
        Dad.Put(his, Dad.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);
        Dad.Put(his, bDad, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        await Dad.Publishing.HandleAsync();
        await Mum.Merging.HandleAsync();
        Mum.Db.ChangeTracker.Clear();

        Assert.DoesNotContain(Mum.Db.AlbumMembers.AsNoTracking(), member => member.AssetId == bMum.Id);

        DecisionSet published = await PublishedAsync(Dad);
        Assert.DoesNotContain(published.Memberships, membership => membership.Photo == Dad.KeyOf(bDad));
    }

    /// <summary>
    /// A photograph put back after being taken out travels with the album.
    /// </summary>
    /// <remarks>
    /// Putting it back does not take the refusal away - nothing does - so a
    /// rule that let go of every refused photograph would let go of this one
    /// too. Only what Dad's own rebuild put there is let go. Mum putting it back
    /// is an answer with her name on it, and it lands.
    /// </remarks>
    [Fact]
    public async Task APhotographPutBackAfterBeingTakenOutTravels()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        Asset aMum = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Asset bMum = Mum.Prepared(@"2019\b.jpg", "bbb.jpg");
        Mum.Put(hers, aMum, Monday);
        Mum.Put(hers, bMum, Monday);
        await Mum.Albums.RemoveAsync(hers.Id, [bMum.Id]);
        await Mum.Albums.AddAsync(hers.Id, [bMum.Id]);
        Mum.Db.ChangeTracker.Clear();

        Album his = Dad.Suggestion("March 2019", Days, kept: false);
        Asset bDad = Dad.Prepared(@"2019\b.jpg", "bbb.jpg");
        Dad.Put(his, Dad.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);
        Dad.Put(his, bDad, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        await Dad.Publishing.HandleAsync();

        DecisionSet published = await PublishedAsync(Dad);
        SharedAlbumMembership putBack =
            Assert.Single(published.Memberships, membership => membership.Photo == Dad.KeyOf(bDad));

        Assert.Equal(his.PublicId, putBack.Album);
        Assert.Equal(Mum.MachineId, putBack.DecidedBy);
        Assert.True(await Dad.NothingPlannedAsync(), "the next merge should find nothing to do");
    }

    /// <summary>
    /// A photograph only the laptop taking the album in had grouped there never
    /// outranks somebody filing it elsewhere.
    /// </summary>
    /// <remarks>
    /// Passed before keeping travelled, and has to go on passing. Once Dad's
    /// suggestion is kept, everything in it is published as his - and a
    /// photograph his own rebuild put there, dated the moment that scan ran,
    /// would beat Gran filing it in an album of her own two days earlier. What
    /// the app placed is dated as early as a date can be when the album is
    /// taken in, so anything a person placed beats it.
    /// </remarks>
    [Fact]
    public async Task APhotographOnlyTheAdopterGroupedNeverOutranksAFiling()
    {
        Library gran = _house.Add("Gran");

        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        Album his = Dad.Suggestion("March 2019", Days, kept: false);
        Asset dadsQ = Dad.Prepared(@"2019\q.jpg", "qqq.jpg");
        Dad.Put(his, Dad.Prepared(@"2019\a.jpg", "aaa.jpg"), Wednesday);
        Dad.Put(his, dadsQ, Thursday);

        Album best = gran.Album("Best shots", Tuesday);
        Asset gransQ = gran.Prepared(@"2019\q.jpg", "qqq.jpg");
        gran.Put(best, gransQ, Tuesday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        await Dad.Publishing.HandleAsync();
        await gran.Publishing.HandleAsync();
        await gran.Merging.HandleAsync();
        await Dad.Merging.HandleAsync();
        await gran.Merging.HandleAsync();

        Assert.Equal(
            best.Id,
            gran.Db.AlbumMembers.AsNoTracking().Single(member => member.AssetId == gransQ.Id).AlbumId);

        Album dadsBest = Dad.Db.Albums.AsNoTracking().Single(album => album.PublicId == best.PublicId);
        Assert.Equal(
            dadsBest.Id,
            Dad.Db.AlbumMembers.AsNoTracking().Single(member => member.AssetId == dadsQ.Id).AlbumId);
    }

    /// <summary>
    /// A dismissal made after the keep takes the kept album away on the
    /// keeper's laptop.
    /// </summary>
    /// <remarks>
    /// Whichever came last stands. The album goes outright rather than leaving
    /// a tombstone: a tombstone means "never suggest these days again", which
    /// is what deleting a kept album says and not what dismissing a suggestion
    /// says. Dad's refusals arrive in the same merge, so the photographs he
    /// refused are not suggested for those days again, and the days can still
    /// come back from any others - exactly as after a dismissal at home - with
    /// nothing in the way of the row a scan adds for them.
    /// </remarks>
    [Fact]
    public async Task ADismissalAfterTheKeepRemovesTheAlbumOnTheKeepersLaptop()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        Asset a = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Mum.Put(hers, a, Monday);

        Album his = Dad.Suggestion("March 2019", Days, kept: false);
        Dad.Put(his, Dad.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);
        await Dad.Albums.DismissAsync(his.Id);
        Dad.Db.ChangeTracker.Clear();

        await Dad.Publishing.HandleAsync();
        await Mum.Merging.HandleAsync();
        Mum.Db.ChangeTracker.Clear();

        Assert.DoesNotContain(
            Mum.Db.Albums.IgnoreQueryFilters().AsNoTracking(), album => album.ProposalKey == Days);
        Assert.Empty(Mum.Db.AlbumMembers.AsNoTracking());
        Assert.Contains(
            Mum.Db.AlbumRejections.AsNoTracking(),
            rejection => rejection.AssetId == a.Id && rejection.ProposalKey == Days);

        AlbumDismissal dismissal = Assert.Single(Mum.Db.AlbumDismissals.AsNoTracking());
        Assert.Equal(Days, dismissal.ProposalKey);
        Assert.Equal(Dad.MachineId, dismissal.DismissedBy);
        Assert.True(await Mum.NothingPlannedAsync(), "the next merge should find nothing to do");

        await Mum.Albums.SaveProposalsAsync([Proposal(a)]);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        Assert.True(await Dad.NothingPlannedAsync(), "the house must converge, not ask for ever");
    }

    /// <summary>
    /// A kept album dismissed elsewhere lets go of the moves that were waiting
    /// for it.
    /// </summary>
    /// <remarks>
    /// A photograph Mum put into the album that Dad has not indexed yet waits on
    /// his laptop, under his album. Once the album goes nothing will ever place
    /// that answer, and keeping it would leave it waiting for good - a count on
    /// the Sync screen that never clears.
    /// </remarks>
    [Fact]
    public async Task AKeptAlbumDismissedElsewhereLetsGoOfTheMovesWaitingForIt()
    {
        Library gran = _house.Add("Gran");

        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);
        Mum.Put(hers, Mum.Prepared(@"2019\c.jpg", "ccc.jpg"), Monday);

        Album his = Dad.Suggestion("March 2019", Days, kept: false);
        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        Dad.Db.ChangeTracker.Clear();

        string part = his.PublicId.ToString("D");
        Assert.Contains(Dad.Db.HeldDecisions.AsNoTracking(), held => held.Part == part);

        gran.Dismissal(Days, Tuesday);
        await gran.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        Dad.Db.ChangeTracker.Clear();

        Assert.False(Dad.Db.Albums.IgnoreQueryFilters().AsNoTracking().Any(album => album.Id == his.Id));
        Assert.DoesNotContain(Dad.Db.HeldDecisions.AsNoTracking(), held => held.Part == part);
    }

    /// <summary>A keep made after a dismissal brings the album back.</summary>
    [Fact]
    public async Task AKeepAfterADismissalBringsTheAlbumBack()
    {
        Dad.Dismissal(Days, Monday);
        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Tuesday);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Tuesday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        Dad.Db.ChangeTracker.Clear();

        Album back = Dad.Db.Albums.AsNoTracking().Single(album => album.ProposalKey == Days);
        Assert.Equal(AlbumOrigin.Accepted, back.Origin);
        Assert.Equal(Tuesday, back.KeptUtc);
        Assert.Equal(hers.PublicId, back.PublicId);
        Assert.Equal(back.Id, Assert.Single(Dad.Db.AlbumMembers.AsNoTracking()).AlbumId);

        // The dismissal is remembered all the same: it is still an answer, and
        // a keep that later turns out to be earlier than some other dismissal
        // is settled against all of them.
        Assert.Equal(Monday, Assert.Single(Dad.Db.AlbumDismissals.AsNoTracking()).DismissedUtc);
        Assert.True(await Dad.NothingPlannedAsync(), "the next merge should find nothing to do");
    }

    /// <summary>
    /// Throwing away a suggestion that a Sync has just kept underneath the
    /// screen leaves the kept album where it is.
    /// </summary>
    /// <remarks>
    /// Dad opens the suggestions while a Sync is still reading the shared
    /// folder. The merge then takes in Mum's keep, so the row behind the
    /// suggestion on his screen is a kept album by the time he presses Throw
    /// away. Throwing it away anyway took the album out without a dismissal to
    /// say so - a kept album is not a suggestion, so none was written - and
    /// the very next Sync brought it straight back from Mum's keep. Only a
    /// suggestion can be thrown away, as only a suggestion can be kept, so the
    /// press does nothing and the reload shows the album kept.
    /// </remarks>
    [Fact]
    public async Task ThrowingAwayASuggestionASyncHasJustKeptLeavesTheKeptAlbum()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        Album his = Dad.Suggestion("March 2019", Days, kept: false);
        Dad.Put(his, Dad.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        Dad.Db.ChangeTracker.Clear();

        await Dad.Albums.DismissAsync(his.Id);
        Dad.Db.ChangeTracker.Clear();

        Album kept = Dad.Db.Albums.IgnoreQueryFilters().AsNoTracking().Single(album => album.Id == his.Id);
        Assert.Equal(AlbumOrigin.Accepted, kept.Origin);
        Assert.Null(kept.DeletedUtc);
        Assert.Equal(his.Id, Assert.Single(Dad.Db.AlbumMembers.AsNoTracking()).AlbumId);
        Assert.Empty(Dad.Db.AlbumRejections.AsNoTracking());
        Assert.Empty(Dad.Db.AlbumDismissals.AsNoTracking());
        Assert.True(await Dad.NothingPlannedAsync(), "the next merge should find nothing to do");
    }

    /// <summary>
    /// Whichever answer about a suggestion came last stands on every laptop in
    /// the house.
    /// </summary>
    /// <remarks>
    /// Three laptops, every answer given before anybody syncs, then two rounds
    /// of everybody pressing Sync. Kept, dismissed and kept again ends kept
    /// everywhere, dated by the last keep. Kept twice and then dismissed ends
    /// with the album gone everywhere. Either way the dismissal is remembered
    /// on every laptop.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhicheverAnswerCameLastStandsOnAllThreeLaptops(bool dismissedLast)
    {
        Library gran = _house.Add("Gran");

        DateTime granKept = dismissedLast ? Tuesday : Wednesday;
        DateTime dadDismissed = dismissedLast ? Wednesday : Tuesday;

        Mum.Put(
            Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday),
            Mum.Prepared(@"2019\a.jpg", "aaa.jpg"),
            Monday);

        Dad.Dismissal(Days, dadDismissed);
        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        gran.Put(
            gran.Suggestion("March 2019", Days, kept: true, keptUtc: granKept),
            gran.Prepared(@"2019\a.jpg", "aaa.jpg"),
            granKept);

        Library[] house = [Mum, Dad, gran];

        for (int round = 0; round < 2; round++)
        {
            foreach (Library library in house)
            {
                await library.Sharing.HandleAsync();
            }
        }

        foreach (Library library in house)
        {
            library.Db.ChangeTracker.Clear();

            List<Album> forTheseDays =
            [
                .. library.Db.Albums.IgnoreQueryFilters().AsNoTracking()
                    .Where(album => album.ProposalKey == Days),
            ];

            if (dismissedLast)
            {
                Assert.Empty(forTheseDays);
            }
            else
            {
                Album kept = Assert.Single(forTheseDays);
                Assert.Equal(AlbumOrigin.Accepted, kept.Origin);
                Assert.Null(kept.DeletedUtc);
                Assert.Equal(Wednesday, kept.KeptUtc);
            }

            Assert.Equal(
                dadDismissed,
                Assert.Single(library.Db.AlbumDismissals.AsNoTracking()).DismissedUtc);

            Assert.True(
                await library.NothingPlannedAsync(),
                $"{library.Name} should have nothing left to do");
        }
    }

    /// <summary>
    /// Deleting a kept suggestion does not reach a laptop still on the previous
    /// release, and still reaches every laptop on this one.
    /// </summary>
    /// <remarks>
    /// The previous release turns its own suggestion for those days into a
    /// tombstone when it hears of the deletion, and its next scan then fails on
    /// the tombstone's key - that scan and every one after it. So the deletion
    /// is set aside in the file, where that release does not look. This release
    /// reads it from there, and its own scans read a tombstone's key as the
    /// answer it is.
    /// </remarks>
    [Fact]
    public async Task DeletingAKeptSuggestionDoesNotReachThePreviousRelease()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        await Mum.Albums.DeleteAsync(hers.Id);

        Album his = Dad.Suggestion("March 2019", Days, kept: false);
        Asset a = Dad.Prepared(@"2019\a.jpg", "aaa.jpg");
        Asset b = Dad.Prepared(@"2019\b.jpg", "bbb.jpg");
        Dad.Put(his, a, Monday);
        Dad.Put(his, b, Monday);

        await Mum.Publishing.HandleAsync();

        using (JsonDocument file = JsonDocument.Parse(PublishedText(Mum)))
        {
            Assert.DoesNotContain(
                file.RootElement.GetProperty("Albums").EnumerateArray(),
                album => album.TryGetProperty("ProposalKey", out _)
                      && album.TryGetProperty("DeletedUtc", out _));

            JsonElement setAside =
                Assert.Single(file.RootElement.GetProperty("DeletedSuggestions").EnumerateArray());
            Assert.Equal(Days, setAside.GetProperty("ProposalKey").GetString());
        }

        await Dad.Merging.HandleAsync();
        Dad.Db.ChangeTracker.Clear();

        Assert.NotNull(
            Dad.Db.Albums.IgnoreQueryFilters().AsNoTracking().Single(album => album.Id == his.Id).DeletedUtc);

        await Dad.Albums.SaveProposalsAsync([Proposal(a, b)]);

        Assert.Empty(Dad.Db.Albums.AsNoTracking());
    }

    /// <summary>What a library last published to the shared folder, as text.</summary>
    private string PublishedText(Library author)
    {
        using FileStream file = File.OpenRead(PathOf(author));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    /// <summary>What a library last published to the shared folder, read back.</summary>
    private async Task<DecisionSet> PublishedAsync(Library author)
    {
        await using FileStream file = File.OpenRead(PathOf(author));
        return await DecisionSetFile.ReadAsync(file);
    }

    private string PathOf(Library author) =>
        Path.Combine(
            _house.SharedFolder,
            SharedFolderExchange.AnswersFolder,
            $"{author.MachineId:D}{DecisionSetFile.Extension}");

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

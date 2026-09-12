using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Shelves of albums crossing between two machines.
/// </summary>
/// <remarks>
/// Collections were built in PRP 13, after sharing shipped, and did not travel:
/// one laptop grouped its albums and every other one in the house stayed flat.
/// The row was made ready for this from its first migration - a public identity,
/// a named date and a tombstone - so what is argued out here is the merge rather
/// than the schema.
///
/// <para>The case that decides the shape is the last one: renaming an album and
/// moving it are two decisions, and settling both on the name's date hands every
/// argument about shelves to whoever typed last.</para>
/// </remarks>
public sealed class SharedCollectionsTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tuesday = new(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task AShelfAndTheAlbumsOnItCrossToTheOtherMachine()
    {
        Collection holidays = Mum.Collection("Holidays", Monday);
        Album bali = Mum.Album("Bali", Monday);
        Mum.Shelve(bali, holidays, Monday);

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Dad.Merging.HandleAsync();

        Collection landed = Assert.Single(Dad.Db.Collections);
        Assert.Equal("Holidays", landed.Name);
        Assert.Equal(holidays.PublicId, landed.PublicId);

        Album album = Assert.Single(Dad.Db.Albums);
        Assert.Equal(landed.Id, album.CollectionId);

        Assert.Equal(1, merged.Outcome.CollectionsChanged);
        Assert.Contains("1 collection", merged.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Merging twice changes nothing the second time.
    /// </summary>
    /// <remarks>
    /// Asserted on the plan rather than on the write counter, because the two
    /// say different things. A counter of zero only means nothing was written;
    /// a plan that is still full means this library and that one have not
    /// actually agreed, and every merge from now until somebody notices will
    /// keep trying to settle the same shelf for ever.
    /// </remarks>
    [Fact]
    public async Task MergingTwiceLeavesNothingLeftToSettle()
    {
        Collection holidays = Mum.Collection("Holidays", Monday);
        Mum.Shelve(Mum.Album("Bali", Monday), holidays, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        MergeResult again = await Dad.Merging.HandleAsync();

        Assert.Equal(0, again.Outcome.CollectionsChanged);
        Assert.Single(Dad.Db.Collections);

        Assert.True(await PlanIsEmptyAsync(Dad), "the merge plan should have nothing left in it");
    }

    /// <summary>
    /// The state every pair of libraries in the house upgrades from.
    /// </summary>
    /// <remarks>
    /// Collections never travelled, so each machine minted its own identity for
    /// its own "Holidays". <c>Collections.Name</c> is a unique index over the
    /// shelves still on the wall, so the first merge after this release is the
    /// one that has to survive two of them - and a merge that throws here takes
    /// the albums, the memberships, the eras and the waiting answers down with
    /// it, on this press and on every press afterwards.
    ///
    /// <para>Numbered rather than refused, the way a second person of one name
    /// already is.</para>
    /// </remarks>
    [Fact]
    public async Task TwoMachinesThatEachMadeTheirOwnHolidaysShelfBothSurvive()
    {
        Mum.Collection("Holidays", Monday);
        Dad.Collection("Holidays", Monday);
        Mum.Shelve(Mum.Album("Bali", Monday), Mum.Db.Collections.Single(), Monday);

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.Empty(merged.Outcome.Refused);

        // Both shelves are still there, and neither has lost its name entirely.
        List<Collection> shelves = [.. Dad.Db.Collections.OrderBy(shelf => shelf.Id)];
        Assert.Equal(2, shelves.Count);
        Assert.Equal("Holidays", shelves[0].Name);
        Assert.Equal("Holidays (2)", shelves[1].Name);

        // And the album came with it rather than being lost to the crash.
        Assert.Single(Dad.Db.Albums);

        Assert.True(await PlanIsEmptyAsync(Dad), "the merge plan should have nothing left in it");
    }

    /// <summary>
    /// Two machines that already agree where an album sits, and disagree only
    /// about when it was put there, settle that too.
    /// </summary>
    /// <remarks>
    /// The date is not cosmetic: it is what the next disagreement is judged on.
    /// Left unsettled, the two libraries never actually agree - the merge keeps
    /// planning the same album for ever and "merging twice changes nothing"
    /// quietly stops being true.
    /// </remarks>
    [Fact]
    public async Task TwoMachinesAgreeingOnTheShelfAlsoSettleWhenItWasShelved()
    {
        Guid id = Guid.NewGuid();
        Guid shelf = Guid.NewGuid();

        Collection hers = Mum.Collection("Holidays", Monday, shelf);
        Album theirs = Mum.Album("Bali", Monday, id);
        Mum.Shelve(theirs, hers, Tuesday);

        Collection his = Dad.Collection("Holidays", Monday, shelf);
        Album ours = Dad.Album("Bali", Monday, id);
        Dad.Shelve(ours, his, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Equal(Tuesday, Assert.Single(Dad.Db.Albums).ShelvedUtc);
        Assert.True(await PlanIsEmptyAsync(Dad), "the merge plan should have nothing left in it");
    }

    /// <summary>
    /// A shelf whose name settles to what it already says still settles its date.
    /// </summary>
    [Fact]
    public async Task AShelfBothMachinesNameTheSameAlsoSettlesWhenItWasNamed()
    {
        Guid shelf = Guid.NewGuid();

        Mum.Collection("Holidays", Tuesday, shelf);
        Dad.Collection("Holidays", Monday, shelf);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Equal(Tuesday, Assert.Single(Dad.Db.Collections).NamedUtc);
        Assert.True(await PlanIsEmptyAsync(Dad), "the merge plan should have nothing left in it");
    }

    /// <summary>What one more merge would still find to do, if anything.</summary>
    private static async Task<bool> PlanIsEmptyAsync(Library library)
    {
        MergeResult again = await library.Merging.HandleAsync();
        return again.Outcome.ChangedNothing;
    }

    /// <summary>
    /// The later name wins, the way a person's and an album's do.
    /// </summary>
    [Fact]
    public async Task TheShelfRenamedLastIsTheNameBothMachinesKeep()
    {
        Guid shelf = Guid.NewGuid();
        Mum.Collection("Holidays", Monday, shelf);
        Dad.Collection("Trips", Tuesday, shelf);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Equal("Trips", Assert.Single(Dad.Db.Collections).Name);

        // And it travels back, so the house does not sit disagreeing.
        await Dad.Publishing.HandleAsync();
        await Mum.Merging.HandleAsync();

        Assert.Equal("Trips", Assert.Single(Mum.Db.Collections).Name);
    }

    /// <summary>
    /// Taking a shelf away takes it away everywhere, and leaves the albums.
    /// </summary>
    /// <remarks>
    /// The albums are the point. Removing a collection has never removed what
    /// was on it, and a merge that deleted somebody's albums because another
    /// machine tidied a shelf would be the one thing this feature promises not
    /// to do.
    /// </remarks>
    [Fact]
    public async Task RemovingAShelfTakesTheAlbumsOffItRatherThanDeletingThem()
    {
        Guid shelf = Guid.NewGuid();

        Collection hers = Mum.Collection("Holidays", Monday, shelf);
        Mum.Shelve(Mum.Album("Bali", Monday), hers, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Single(Dad.Db.Albums);
        Assert.NotNull(Assert.Single(Dad.Db.Albums).CollectionId);

        Mum.Remove(hers, Tuesday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Collection gone = Assert.Single(Dad.Db.Collections.IgnoreQueryFilters());
        Assert.Equal(Tuesday, gone.DeletedUtc);

        Album survivor = Assert.Single(Dad.Db.Albums);
        Assert.Null(survivor.CollectionId);
    }

    /// <summary>
    /// A tombstone is not undone by a machine that still holds the shelf.
    /// </summary>
    [Fact]
    public async Task AShelfSomebodyRemovedDoesNotComeBackFromTheOtherMachine()
    {
        Guid shelf = Guid.NewGuid();
        Collection hers = Mum.Collection("Holidays", Monday, shelf);
        Dad.Collection("Holidays", Monday, shelf);

        Mum.Remove(hers, Tuesday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Empty(Dad.Db.Collections);
        Assert.Single(Dad.Db.Collections.IgnoreQueryFilters());
    }

    /// <summary>
    /// The whole reason <c>ShelvedUtc</c> is a column of its own.
    /// </summary>
    /// <remarks>
    /// One machine renames an album; the other moves it to a shelf, earlier.
    /// Settling the shelf on the name's date would let the rename carry its own
    /// stale "on no shelf" and quietly undo a move nobody argued with.
    /// </remarks>
    [Fact]
    public async Task RenamingAnAlbumDoesNotUndoSomebodyElseShelvingIt()
    {
        Guid id = Guid.NewGuid();
        Guid shelf = Guid.NewGuid();

        // Dad shelved it on Monday and has never renamed it since.
        Collection his = Dad.Collection("Holidays", Monday, shelf);
        Album ours = Dad.Album("Bali", Monday, id);
        Dad.Shelve(ours, his, Monday);

        // Mum renamed it on Tuesday, and has no shelves at all.
        Mum.Album("Bali 2020", Tuesday, id);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Album settled = Assert.Single(Dad.Db.Albums);

        Assert.Equal("Bali 2020", settled.Name);
        Assert.Equal(his.Id, settled.CollectionId);
    }

    /// <summary>
    /// Taking an album off a shelf beats an older decision to put it on one.
    /// </summary>
    /// <remarks>
    /// An unshelving has a moment behind it, which is why the column takes a
    /// date rather than simply going null. Without one it would be
    /// indistinguishable from a library that had never shelved the album, and
    /// would lose every argument to the machine that had.
    /// </remarks>
    [Fact]
    public async Task TakingAnAlbumOffAShelfBeatsAnOlderShelving()
    {
        Guid id = Guid.NewGuid();
        Guid shelf = Guid.NewGuid();

        Collection his = Dad.Collection("Holidays", Monday, shelf);
        Dad.Shelve(Dad.Album("Bali", Monday, id), his, Monday);

        Collection hers = Mum.Collection("Holidays", Monday, shelf);
        Album theirs = Mum.Album("Bali", Monday, id);
        Mum.Shelve(theirs, hers, Monday);
        Mum.Shelve(theirs, null, Tuesday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Null(Assert.Single(Dad.Db.Albums).CollectionId);
    }

    /// <summary>
    /// A machine that has never heard of a shelf files the album under none.
    /// </summary>
    /// <remarks>
    /// It cannot happen while both sides publish everything they hold, and the
    /// data allows it anyway - there is deliberately no foreign key on that
    /// column. Inventing a shelf nobody made would be worse than reading it as
    /// what the wall already reads an unknown identity as.
    /// </remarks>
    [Fact]
    public async Task AnAlbumOnAShelfNobodyHasHeardOfLandsOnNoShelf()
    {
        Album bali = Mum.Album("Bali", Monday);
        Mum.Shelve(bali, Mum.Collection("Holidays", Monday), Monday);

        await Mum.Publishing.HandleAsync();

        // Every shelf forgotten between publishing and merging, which is the
        // only way to reach this state through the real handlers.
        Mum.Db.Collections.RemoveRange(Mum.Db.Collections.IgnoreQueryFilters());
        Mum.Db.SaveChanges();
        Mum.Db.ChangeTracker.Clear();

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Empty(Dad.Db.Collections);
        Assert.Null(Assert.Single(Dad.Db.Albums).CollectionId);
    }

    public void Dispose() => _house.Dispose();
}

using Microsoft.EntityFrameworkCore;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A photograph taken out of an album comes out on every computer, and stays
/// out until somebody puts it back.
/// </summary>
/// <remarks>
/// Taking a photograph out was remembered as a refusal, and refusals travelled,
/// but nothing on the other computer read one against a membership: its own
/// filing stood, it published it, and the next sync put the photograph straight
/// back where it had been taken out. A refusal later than a membership now
/// beats it, in both directions.
/// </remarks>
public sealed class RemovalsTravelTests : IDisposable
{
    private const string Days = "2019-03-03..2019-03-05";

    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task TakenOutOfAMadeAlbum_ItComesOutOnTheOtherComputerToo()
    {
        (Album hers, Asset a, Album his) = BothHaveBaliWithAPhotograph();

        await Mum.Albums.RemoveAsync(hers.Id, [a.Id]);
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Empty(MembersOf(Dad, his));
        Assert.True(await Dad.NothingPlannedAsync(), "the next merge should find nothing to do");
    }

    [Fact]
    public async Task TheComputerThatHasNotHeardYet_DoesNotPutItBack()
    {
        (Album hers, Asset a, _) = BothHaveBaliWithAPhotograph();

        await Mum.Albums.RemoveAsync(hers.Id, [a.Id]);

        // Dad syncs before he has taken Mum's answers, so his file still says
        // the photograph is in the album.
        await Dad.Publishing.HandleAsync();
        await Mum.Merging.HandleAsync();

        Assert.Empty(MembersOf(Mum, hers));
        Assert.True(await Mum.NothingPlannedAsync(), "the next merge should find nothing to do");
    }

    [Fact]
    public async Task PutBackAfterwards_ItIsBackEverywhere()
    {
        (Album hers, Asset a, Album his) = BothHaveBaliWithAPhotograph();

        await Mum.Albums.RemoveAsync(hers.Id, [a.Id]);
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        // The refusal is never cleared; the later membership is what stands.
        await Mum.Albums.AddAsync(hers.Id, [a.Id]);
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Single(MembersOf(Dad, his));
        Assert.True(await Dad.NothingPlannedAsync(), "the next merge should find nothing to do");
    }

    [Fact]
    public async Task TakenOutOfAKeptSuggestion_ItComesOutOnTheOtherComputerToo()
    {
        Album hers = Mum.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        Asset a = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Mum.Put(hers, a, Monday);

        Album his = Dad.Suggestion("March 2019", Days, kept: true, keptUtc: Monday);
        Dad.Put(his, Dad.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        await Mum.Albums.RemoveAsync(hers.Id, [a.Id]);
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Empty(MembersOf(Dad, his));
    }

    [Fact]
    public async Task ARefusalOfAnotherAlbum_LeavesThisOneAlone()
    {
        (_, Asset a, Album his) = BothHaveBaliWithAPhotograph();
        Album garden = Mum.Album("Garden", Monday);
        Mum.Put(garden, Mum.Prepared(@"2019\b.jpg", "bbb.jpg"), Monday);

        // Refused from a different album, so it says nothing about Bali.
        await Mum.Albums.RefuseAsync(garden.Id, [a.Id]);
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Single(MembersOf(Dad, his));
    }

    /// <summary>The same album, made once and synced, with one photograph in it on both.</summary>
    private (Album Hers, Asset Photo, Album His) BothHaveBaliWithAPhotograph()
    {
        var bali = Guid.NewGuid();

        Album hers = Mum.Album("Bali", Monday, bali);
        Asset a = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Mum.Put(hers, a, Monday);

        Album his = Dad.Album("Bali", Monday, bali);
        Dad.Put(his, Dad.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        return (hers, a, his);
    }

    private static List<AlbumMember> MembersOf(Library library, Album album)
    {
        library.Db.ChangeTracker.Clear();
        return [.. library.Db.AlbumMembers.AsNoTracking().Where(member => member.AlbumId == album.Id)];
    }

    public void Dispose() => _house.Dispose();
}

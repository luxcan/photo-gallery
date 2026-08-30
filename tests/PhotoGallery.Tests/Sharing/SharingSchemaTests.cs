using Microsoft.EntityFrameworkCore;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// The rules sharing needs the database itself to hold.
/// </summary>
/// <remarks>
/// Asserted against real SQLite with the migrations applied, because every claim
/// here is a claim about the schema: the point of a unique index is that it
/// holds whichever handler forgets, and no in-memory model would prove it.
///
/// <para>Two of them are the whole reason the columns exist. A tombstone has to
/// outlive the row it describes or a deleted person walks back in from the next
/// machine that still holds them, and a held answer has to be one row however
/// many times it is merged or nothing about merging is idempotent.</para>
/// </remarks>
public sealed class SharingSchemaTests : IDisposable
{
    private static readonly DateTime Morning = new(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly TwoLibraries _house = new();

    private Library Mum => _house.Mum;

    [Fact]
    public void ADeletedPersonLeavesSomethingBehind()
    {
        Person ana = Mum.Person("Ana");

        Forget(ana);

        // Gone from every query in the app without one of them saying so...
        Assert.Empty(Mum.Db.People);

        // ...and still there for the one thing that has to know the difference
        // between deleted and never known.
        Person tombstone = Mum.Db.People.IgnoreQueryFilters().Single();
        Assert.Equal(ana.PublicId, tombstone.PublicId);
        Assert.NotNull(tombstone.DeletedUtc);
    }

    [Fact]
    public void ADeletedPersonDoesNotHoldTheirNameHostage()
    {
        // Ordinary and easy to get wrong: an unfiltered unique index on the name
        // would refuse this, and the refusal would read as a bug rather than as
        // a tombstone.
        Forget(Mum.Person("Ana"));

        Person again = Mum.Person("Ana");

        Assert.NotEqual(Guid.Empty, again.PublicId);
        Assert.Equal("Ana", Assert.Single(Mum.Db.People).DisplayName);
    }

    [Fact]
    public void ADeletedPersonKeepsNobody()
    {
        // Their faces go back to being nobody in particular. Left in place the
        // rows would still be counted as confirmed by every query that asks.
        Asset photo = Mum.Photo(@"2019\a.jpg");
        Person ana = Mum.Person("Ana");
        Mum.Answer(Mum.Face(photo, new FaceBounds(10, 10, 40, 40)), ana, AssignmentSource.Confirmed, Morning);

        Forget(ana);

        Assert.Empty(Mum.Db.FaceAssignments);
    }

    [Fact]
    public void TwoMachinesNamingAnaSeparatelyProduceTwoIdentities()
    {
        // Two Anas is a real thing in a family, so the merge must be able to see
        // that these are not known to be the same person.
        Assert.NotEqual(Mum.Person("Ana").PublicId, _house.Dad.Person("Ana").PublicId);
    }

    [Fact]
    public async Task OnePersonCannotBecomeTwoRows()
    {
        Person ana = Mum.Person("Ana");
        Mum.Db.People.Add(new Person { PublicId = ana.PublicId, DisplayName = "Ana Lim" });

        await Assert.ThrowsAsync<DbUpdateException>(() => Mum.Db.SaveChangesAsync());
    }

    [Fact]
    public void ADeletedAlbumLeavesATombstoneAndFreesItsPhotographs()
    {
        // A tombstone holding photographs against the one-album rule would be the
        // hostage the dismissal path already refuses to take.
        Asset photo = Mum.Photo(@"2019\a.jpg");
        Collection album = Mum.Album("Genting", Morning);
        Mum.Db.CollectionMembers.Add(new CollectionMember
        {
            AssetId = photo.Id,
            CollectionId = album.Id,
            AddedUtc = Morning,
        });
        Mum.Db.SaveChanges();

        Collection open = Mum.Db.Collections.Include(c => c.Members).Single();
        open.Members.Clear();
        open.DeletedUtc = Morning.AddHours(1);
        Mum.Db.SaveChanges();
        Mum.Db.ChangeTracker.Clear();

        Assert.Empty(Mum.Db.Collections);
        Assert.Empty(Mum.Db.CollectionMembers);
        Assert.NotNull(Mum.Db.Collections.IgnoreQueryFilters().Single().DeletedUtc);
    }

    [Fact]
    public async Task OneAnswerPerFaceHoweverManyTimesItArrives()
    {
        // What makes merging twice change nothing the second time. Without the
        // key the table would grow with the number of times somebody pressed the
        // button rather than with what anybody decided.
        Held(@"2019\a.jpg", part: "10,10,40,40");
        await Mum.Db.SaveChangesAsync();

        Held(@"2019\a.jpg", part: "10,10,40,40");

        await Assert.ThrowsAsync<DbUpdateException>(() => Mum.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task TwoFacesInOnePhotographAreTwoAnswers()
    {
        // The other half of the same rule, and the reason the key is not the
        // photograph alone: the busiest photograph in this library holds 32.
        Held(@"2019\a.jpg", part: "10,10,40,40");
        Held(@"2019\a.jpg", part: "80,10,40,40");

        await Mum.Db.SaveChangesAsync();

        Assert.Equal(2, await Mum.Db.HeldDecisions.CountAsync());
    }

    [Fact]
    public async Task AHeldAnswerNeedsNoPhotographToPointAt()
    {
        // The whole point of the table is that the picture is not here yet, so a
        // foreign key would refuse exactly the row that matters.
        Held(@"2028\not-scanned-yet.jpg", part: string.Empty);

        await Mum.Db.SaveChangesAsync();

        HeldDecision waiting = await Mum.Db.HeldDecisions.SingleAsync();
        Assert.Equal(new AssetKey(Mum.SharedSourceId, @"2028\not-scanned-yet.jpg"), waiting.Key);
        Assert.Empty(Mum.Db.Assets);
    }

    [Fact]
    public async Task OneMachineIsOneRowHoweverManyTimesItIsHeardFrom()
    {
        Guid dad = _house.Dad.MachineId;
        Mum.Db.KnownMachines.Add(new KnownMachine { MachineId = dad, Name = "Dad's laptop" });
        await Mum.Db.SaveChangesAsync();

        Mum.Db.KnownMachines.Add(new KnownMachine { MachineId = dad, Name = "Dad's laptop" });

        await Assert.ThrowsAsync<DbUpdateException>(() => Mum.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task TwoSourcesCannotBeTheSameFolderOnAnotherMachine()
    {
        Mum.Db.PhotoSources.Add(new PhotoSource
        {
            Path = Path.Combine(Mum.Root, "second"),
            SharedId = Mum.SharedSourceId,
            AddedUtc = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => Mum.Db.SaveChangesAsync());
    }

    [Fact]
    public void APhotographNobodyHasTurnedCarriesNoMoment()
    {
        // Null is what makes a turn on the other machine win rather than tie.
        Assert.Null(Mum.Photo(@"2019\a.jpg").RotatedUtc);
    }

    private void Forget(Person person)
    {
        Mum.Db.FaceAssignments.Where(a => a.PersonId == person.Id).ExecuteDelete();
        Mum.Db.Set<PersonEra>().Where(e => e.PersonId == person.Id).ExecuteDelete();
        Mum.Db.People.Where(p => p.Id == person.Id)
            .ExecuteUpdate(s => s.SetProperty(p => p.DeletedUtc, Morning));
        Mum.Db.ChangeTracker.Clear();
    }

    private void Held(string relativePath, string part) =>
        Mum.Db.HeldDecisions.Add(new HeldDecision
        {
            SharedSourceId = Mum.SharedSourceId,
            RelativePath = relativePath,
            Kind = part.Length == 0 ? HeldDecisionKind.Turn : HeldDecisionKind.FaceAnswer,
            Part = part,
            Payload = """{"person":"Ana"}""",
            FromMachine = _house.Dad.MachineId,
            DecidedUtc = Morning,
        });

    public void Dispose() => _house.Dispose();
}

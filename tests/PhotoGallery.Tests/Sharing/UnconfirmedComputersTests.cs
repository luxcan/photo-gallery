using Microsoft.EntityFrameworkCore;
using PhotoGallery.App.Sharing;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A computer nobody here has said to take changes from is held back whole,
/// until somebody does.
/// </summary>
/// <remarks>
/// Every check a sync made was on what a computer's own file says about itself
/// - its id, its version, its clock, a folder in common - and anybody who can
/// write to the sync folder can read an honest laptop's file and copy all of
/// them. One planted file could then delete every person and album here, and
/// the next Sync would pass the deletions on to every other laptop. So the
/// attack under test is exactly that file: Mum's own, copied, given a new id
/// and a tombstone for everything in it.
/// </remarks>
public sealed class UnconfirmedComputersTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task AComputerNobodyHasAnsweredFor_DeletesNobodyAndNoAlbum()
    {
        (Guid ana, Guid bali) = await BothKnowAnaAndBaliAsync();
        Guid forger = await PlantDeletingEverythingAsync();

        ShareResult shared = await Dad.Sharing.HandleAsync();

        Assert.True(Dad.Db.People.Any(person => person.PublicId == ana));
        Assert.True(Dad.Db.Albums.Any(album => album.PublicId == bali));

        // Nor does it pass them on: what Dad publishes is what Dad still holds.
        DecisionSet his = await Dad.Decisions.ReadAsync(
            await PublishDecisionsHandler.ThisMachineAsync(Dad.Index, CancellationToken.None));
        Assert.Null(his.People.Single(person => person.PublicId == ana).DeletedUtc);
        Assert.Null(his.Albums.Single(album => album.PublicId == bali).DeletedUtc);

        // And it says why nothing came, and asks.
        Assert.Equal(["Mum's laptop"], shared.Merged.Unconfirmed);
        Assert.Contains("Say below whether to take changes", shared.Summary, StringComparison.Ordinal);
        Assert.False(shared.Merged.UpToDate);

        SharingStatus status = await Dad.Overview.HandleAsync();
        MachineStanding asked = Assert.Single(status.Machines, machine => machine.IsUnanswered);
        Assert.Equal(forger, asked.MachineId);
    }

    [Fact]
    public async Task OnceSomebodySaysTakeChanges_TheyApply()
    {
        (Guid ana, Guid bali) = await BothKnowAnaAndBaliAsync();
        Guid forger = await PlantDeletingEverythingAsync();
        await Dad.Sharing.HandleAsync();

        await Dad.Deciding.HandleAsync(forger, "Mum's laptop", take: true);
        await Dad.Sharing.HandleAsync();

        // The gate is the only thing that held them: answered for, the same
        // file deletes exactly what it says.
        Assert.False(Dad.Db.People.Any(person => person.PublicId == ana));
        Assert.False(Dad.Db.Albums.Any(album => album.PublicId == bali));
        Assert.DoesNotContain(
            (await Dad.Overview.HandleAsync()).Machines, machine => machine.IsUnanswered);
    }

    [Fact]
    public async Task NotThisOne_KeepsItsFilesUnreadOnEverySync()
    {
        (Guid ana, Guid bali) = await BothKnowAnaAndBaliAsync();
        Guid forger = await PlantDeletingEverythingAsync();
        await Dad.Sharing.HandleAsync();

        await Dad.Deciding.HandleAsync(forger, "Mum's laptop", take: false);
        ShareResult again = await Dad.Sharing.HandleAsync();
        await Dad.Sharing.HandleAsync();

        Assert.True(Dad.Db.People.Any(person => person.PublicId == ana));
        Assert.True(Dad.Db.Albums.Any(album => album.PublicId == bali));

        // Not asked about again, and not news: it was answered.
        Assert.Empty(again.Merged.Unconfirmed);
        Assert.False((await Dad.Looking.HandleAsync()).Any);

        // Still listed, so the answer can be taken back.
        MachineStanding refused = Assert.Single(
            (await Dad.Overview.HandleAsync()).Machines, machine => machine.MachineId == forger);
        Assert.Equal(MachineTrust.Ignored, refused.Trust);
        Assert.False(refused.IsUnanswered);
    }

    [Fact]
    public async Task ANotThisOneCanBeTakenBack()
    {
        (Guid ana, _) = await BothKnowAnaAndBaliAsync();
        Guid forger = await PlantDeletingEverythingAsync();
        await Dad.Sharing.HandleAsync();
        await Dad.Deciding.HandleAsync(forger, "Mum's laptop", take: false);
        await Dad.Sharing.HandleAsync();

        await Dad.Deciding.HandleAsync(forger, "Mum's laptop", take: true);
        await Dad.Sharing.HandleAsync();

        Assert.False(Dad.Db.People.Any(person => person.PublicId == ana));
    }

    [Fact]
    public async Task AComputerSomebodyTrusts_GoesOnSyncingAsBefore()
    {
        // The normal path, beside a planted file: Mum's own changes still land.
        await BothKnowAnaAndBaliAsync();
        await PlantDeletingEverythingAsync();

        Mum.Person("Ben");
        await Mum.Publishing.HandleAsync();
        ShareResult shared = await Dad.Sharing.HandleAsync();

        Assert.True(Dad.Db.People.Any(person => person.DisplayName == "Ben"));
        Assert.Equal(1, shared.Merged.Outcome.PeopleGained);
    }

    [Fact]
    public async Task ItsPicturesAndWhatItsDecodeLearnedAreNotTakenUntilSomebodySays()
    {
        using var strangers = new TwoLibraries();
        strangers.Mum.SharesThrough(strangers.SharedFolder);
        strangers.Dad.SharesThrough(strangers.SharedFolder);

        strangers.Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        strangers.Dad.Photo(@"2019\a.jpg");
        await strangers.Mum.Pooling.GiveAsync();

        PoolResult held = await strangers.Dad.Pooling.TakeAsync();

        Assert.Equal(0, held.Filled);
        Assert.False(strangers.Dad.Holds("aa11.jpg"));
        Assert.Null(strangers.Dad.Db.Assets.AsNoTracking().Single().ThumbnailName);

        strangers.Dad.Trusts(strangers.Mum);
        PoolResult taken = await strangers.Dad.Pooling.TakeAsync();

        Assert.Equal(1, taken.Filled);
        Assert.True(strangers.Dad.Holds("aa11.jpg"));
    }

    [Fact]
    public async Task ItsFacesAreNotTakenUntilSomebodySays()
    {
        using var strangers = new TwoLibraries();
        strangers.Mum.SharesThrough(strangers.SharedFolder);
        strangers.Dad.SharesThrough(strangers.SharedFolder);

        strangers.Mum.Face(strangers.Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        strangers.Dad.Prepared(@"2019\a.jpg", "aa11.jpg");
        await strangers.Mum.Pooling.GiveAsync();

        PoolResult held = await strangers.Dad.Pooling.TakeAsync();

        Assert.Equal(0, held.Faces);
        Assert.Empty(strangers.Dad.Db.Faces);

        strangers.Dad.Trusts(strangers.Mum);
        PoolResult taken = await strangers.Dad.Pooling.TakeAsync();

        Assert.Equal(1, taken.Faces);
    }

    [Fact]
    public async Task ThisComputerCannotBeAnsweredForAsAnotherOne()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Dad.Deciding.HandleAsync(Dad.MachineId, "Dad's laptop", take: true));
    }

    [Fact]
    public async Task ANameATrustedComputerAlreadyUsesIsSaidOutLoud()
    {
        // Exactly the name a planted file would choose.
        await BothKnowAnaAndBaliAsync();
        Guid forger = await PlantDeletingEverythingAsync();
        await Dad.Sharing.HandleAsync();

        MachineStanding claims = Assert.Single(
            (await Dad.Overview.HandleAsync()).Machines, machine => machine.MachineId == forger);

        Assert.True(claims.SharesItsName);
        Assert.Contains(
            "Another computer is also calling itself Mum's laptop",
            SharingViewModel.AskingAbout(claims),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANameNobodyElseUsesIsAskedPlainly()
    {
        MachineStanding gran = new("Gran's laptop", Monday, false)
        {
            MachineId = Guid.NewGuid(),
            Trust = MachineTrust.Undecided,
        };

        Assert.Equal("Take changes from Gran's laptop?", SharingViewModel.AskingAbout(gran));
    }

    /// <summary>
    /// On a new or reinstalled laptop the real one and a copy planted beside it
    /// are both unanswered, and both questions say so - each with when that
    /// computer last shared, which is what tells them apart.
    /// </summary>
    [Fact]
    public async Task TwoUnansweredComputersWithOneName_AreBothCalledOut()
    {
        using var strangers = new TwoLibraries();
        strangers.Mum.SharesThrough(strangers.SharedFolder);
        strangers.Dad.SharesThrough(strangers.SharedFolder);
        strangers.Mum.Person("Ana");
        await strangers.Mum.Publishing.HandleAsync();
        await PlantCopyOfMumAsync(strangers, strangers.Mum.Name);

        await strangers.Dad.Sharing.HandleAsync();

        MachineStanding[] asked =
            [.. (await strangers.Dad.Overview.HandleAsync()).Machines.Where(m => m.IsUnanswered)];

        Assert.Equal(2, asked.Length);
        Assert.All(asked, machine =>
        {
            Assert.Equal("Mum's laptop", machine.Name);
            Assert.True(machine.SharesItsName);

            string question = SharingViewModel.AskingAbout(machine);
            Assert.Contains(
                "Another computer is also calling itself Mum's laptop", question, StringComparison.Ordinal);
            Assert.Contains("which last shared changes on", question, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A planted file that gives itself no name, or only spaces, is still a
    /// computer that can be refused - and the computers somebody trusts go on
    /// syncing beside it.
    /// </summary>
    /// <remarks>
    /// A missing name used to fail the row it is written down in, and with it
    /// every Sync, so the computer could never be refused. A blank one was
    /// written down, and answering it threw past the button and closed the app.
    /// </remarks>
    [Fact]
    public async Task AComputerWithNoName_CanBeRefused_AndTrustedOnesStillSync()
    {
        (Guid ana, Guid bali) = await BothKnowAnaAndBaliAsync();
        Guid blank = await PlantCopyOfMumAsync(_house, "   ");
        Guid nameless = await PlantCopyOfMumAsync(_house, null);

        Mum.Person("Ben");
        await Mum.Publishing.HandleAsync();
        ShareResult shared = await Dad.Sharing.HandleAsync();

        Assert.True(Dad.Db.People.Any(person => person.DisplayName == "Ben"));
        Assert.True(Dad.Db.People.Any(person => person.PublicId == ana));

        MachineStanding[] asked =
            [.. (await Dad.Overview.HandleAsync()).Machines.Where(m => m.IsUnanswered)];
        Assert.Equal(new[] { blank, nameless }.Order(), asked.Select(m => m.MachineId).Order());
        Assert.All(asked, machine => Assert.Equal(KnownMachine.NoName, machine.Name));
        Assert.Equal([KnownMachine.NoName, KnownMachine.NoName], shared.Merged.Unconfirmed);

        // Answered with the name the screen was handed, blank or not.
        await Dad.Deciding.HandleAsync(blank, "   ", take: false);
        await Dad.Deciding.HandleAsync(nameless, null, take: false);
        await Dad.Sharing.HandleAsync();

        Assert.True(Dad.Db.People.Any(person => person.PublicId == ana));
        Assert.True(Dad.Db.Albums.Any(album => album.PublicId == bali));
        Assert.All(
            (await Dad.Overview.HandleAsync()).Machines.Where(m => m.MachineId != Mum.MachineId),
            machine => Assert.Equal(MachineTrust.Ignored, machine.Trust));
    }

    /// <summary>
    /// The row a computer is written down in never takes its name unchecked,
    /// whatever read the file.
    /// </summary>
    [Fact]
    public async Task MeetingAComputerWithNoName_WritesItDownUnderAStandIn()
    {
        Guid nameless = Guid.NewGuid();

        await Dad.Writing.MeetAsync(new MachineIdentity(nameless, null!, "1.0.0", 1));

        KnownMachine met = Dad.Db.KnownMachines.AsNoTracking().Single(row => row.MachineId == nameless);
        Assert.Equal(KnownMachine.NoName, met.Name);
        Assert.Equal(MachineTrust.Undecided, met.Trust);
    }

    [Fact]
    public async Task ANameLongerThanTheColumn_IsCutRatherThanRefused()
    {
        await BothKnowAnaAndBaliAsync();
        Guid forger = await PlantCopyOfMumAsync(_house, new string('x', 400));

        await Dad.Sharing.HandleAsync();

        MachineStanding asked = Assert.Single(
            (await Dad.Overview.HandleAsync()).Machines, machine => machine.MachineId == forger);
        Assert.Equal(KnownMachine.NameLength, asked.Name.Length);
    }

    /// <summary>
    /// A person and an album both laptops already have, through an ordinary
    /// sync between two computers that trust each other.
    /// </summary>
    private async Task<(Guid Person, Guid Album)> BothKnowAnaAndBaliAsync()
    {
        Guid ana = Mum.Person("Ana").PublicId;
        Guid bali = Mum.Album("Bali", Monday).PublicId;
        Mum.Db.ChangeTracker.Clear();

        await Mum.Publishing.HandleAsync();
        await Dad.Sharing.HandleAsync();

        Assert.True(Dad.Db.People.Any(person => person.PublicId == ana));
        Assert.True(Dad.Db.Albums.Any(album => album.PublicId == bali));

        return (ana, bali);
    }

    /// <summary>
    /// What somebody with write access to the sync folder can do without any
    /// laptop: copy Mum's file, give it an id of its own and her name, and
    /// delete everybody and everything in it.
    /// </summary>
    private Task<Guid> PlantDeletingEverythingAsync() => PlantCopyOfMumAsync(_house, Mum.Name);

    /// <summary>
    /// Mum's file copied under a fresh id and whatever name the test chooses -
    /// none at all included - deleting everybody and everything in it.
    /// </summary>
    private static async Task<Guid> PlantCopyOfMumAsync(TwoLibraries house, string? name)
    {
        string answers = Path.Combine(house.SharedFolder, SharedFolderExchange.AnswersFolder);

        DecisionSet honest;
        await using (FileStream file = File.OpenRead(
            Path.Combine(answers, $"{house.Mum.MachineId:D}{DecisionSetFile.Extension}")))
        {
            honest = await DecisionSetFile.ReadAsync(file);
        }

        DateTime earlier = DateTime.UtcNow.AddMinutes(-5);
        var forger = new MachineIdentity(
            Guid.NewGuid(), name!, honest.Machine.AppVersion, honest.Machine.SchemaVersion);

        DecisionSet forged = honest with
        {
            Machine = forger,
            People = [.. honest.People.Select(person => person with { DeletedUtc = earlier })],
            Albums = [.. honest.Albums.Select(album => album with { DeletedUtc = earlier })],
        };

        await using (FileStream file = File.Create(
            Path.Combine(answers, $"{forger.Id:D}{DecisionSetFile.Extension}")))
        {
            await DecisionSetFile.WriteAsync(file, forged);
        }

        return forger.Id;
    }

    public void Dispose() => _house.Dispose();
}

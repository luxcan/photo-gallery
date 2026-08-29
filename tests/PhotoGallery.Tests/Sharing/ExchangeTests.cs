using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Two libraries, a folder between them, and answers actually crossing it.
/// </summary>
/// <remarks>
/// Everything here runs the real stack - the real reader, the real writer, the
/// real file written to a real folder and read back - because the merge rules
/// were already proven as pure functions and what is left to doubt is exactly
/// the part those tests could not touch: whether a decision survives being
/// written down, and whether applying a plan writes the rows it says it does.
/// </remarks>
public sealed class ExchangeTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task ANameGivenOnOneMachineArrivesOnTheOther()
    {
        // The whole feature: no scan in between, and both machines had already
        // indexed the photograph.
        Both(@"2019\a.jpg");
        Person ana = Mum.Person("Ana");
        Mum.Answer(Mum.Db.Faces.Single(), ana, AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.True(merged.Merged);
        Assert.Equal(1, merged.Outcome.PeopleGained);
        Assert.Equal(1, merged.Outcome.NamesGained);

        Person arrived = Dad.Db.People.Single();
        Assert.Equal("Ana", arrived.DisplayName);
        Assert.Equal(ana.PublicId, arrived.PublicId);

        FaceAssignment answer = Dad.Db.FaceAssignments.Single();
        Assert.Equal(AssignmentSource.Confirmed, answer.Source);
        Assert.Equal(Monday, answer.DecidedUtc);
    }

    [Fact]
    public async Task AnAnswerKeepsTheMachineThatMadeItWhenItIsPassedOn()
    {
        // What makes three machines converge with no machinery for it. An answer
        // that lost its author on the way through would be republished as the
        // forwarder's own and would start settling ties it has no business
        // settling.
        Both(@"2019\a.jpg");
        Mum.Answer(
            Mum.Db.Faces.Single(), Mum.Person("Ana"), AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Equal(Mum.MachineId, Dad.Db.FaceAssignments.Single().DecidedBy);

        // And it is still hers when Dad passes it on.
        await Dad.Publishing.HandleAsync();

        // A third machine that has never heard from Mum, and only ever reads
        // Dad's file.
        Library ana = _house.Add("Ana's laptop");
        Face(ana, Photo(ana, @"2019\a.jpg"));

        await ana.Merging.HandleAsync();

        Assert.Equal(Mum.MachineId, ana.Db.FaceAssignments.Single().DecidedBy);
    }

    [Fact]
    public async Task MergingTwiceChangesNothingTheSecondTime()
    {
        Both(@"2019\a.jpg");
        Mum.Answer(
            Mum.Db.Faces.Single(), Mum.Person("Ana"), AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        MergeResult again = await Dad.Merging.HandleAsync();

        Assert.True(again.Outcome.ChangedNothing);
        Assert.Single(Dad.Db.FaceAssignments);
    }

    [Fact]
    public async Task AnAnswerAboutAPhotographThisLibraryHasNotIndexedWaits()
    {
        // The single most important merge rule, and the one that makes the order
        // of operations impossible to get wrong.
        Face(Mum, Photo(Mum, @"2026 Phone Dump\b.jpg"));
        Mum.Answer(
            Mum.Db.Faces.Single(), Mum.Person("Ana"), AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.Equal(1, merged.Outcome.Held);
        Assert.Empty(Dad.Db.FaceAssignments);

        HeldDecision waiting = Dad.Db.HeldDecisions.Single();
        Assert.Equal(@"2026 Phone Dump\b.jpg", waiting.RelativePath);
        Assert.Equal(HeldDecisionKind.FaceAnswer, waiting.Kind);
        Assert.Equal(Mum.MachineId, waiting.FromMachine);

        // And waiting twice is still one answer waiting.
        await Dad.Merging.HandleAsync();
        Assert.Single(Dad.Db.HeldDecisions);
    }

    [Fact]
    public async Task TheSummarySaysWhatChangedAndWhatIsStillWaiting()
    {
        // A merge that says nothing is a merge nobody can trust or undo.
        Both(@"2019\a.jpg");
        Face(Mum, Photo(Mum, @"2026 Phone Dump\b.jpg"));

        Person ana = Mum.Person("Ana");
        foreach (Face face in Mum.Db.Faces.ToList())
        {
            Mum.Answer(face, ana, AssignmentSource.Confirmed, Monday);
        }

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.Contains("1 name", merged.Summary);
        Assert.Contains("1 person", merged.Summary);
        Assert.Contains("waiting", merged.Summary);
    }

    [Fact]
    public async Task AMachineWithNothingToSayYetIsNotAFailure()
    {
        Both(@"2019\a.jpg");

        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.True(merged.Merged);
        Assert.Equal("No other computer has shared anything yet.", merged.Summary);
    }

    [Fact]
    public async Task WithNoFolderChosenNothingHappensAndItSaysWhy()
    {
        using var alone = new TwoLibraries();

        PublishResult published = await alone.Mum.Publishing.HandleAsync();
        MergeResult merged = await alone.Mum.Merging.HandleAsync();

        Assert.False(published.Published);
        Assert.False(merged.Merged);
        Assert.Contains("Choose a folder", merged.Summary);
    }

    [Fact]
    public async Task AMachineDoesNotReadItsOwnFileBackIn()
    {
        Both(@"2019\a.jpg");
        Mum.Answer(
            Mum.Db.Faces.Single(), Mum.Person("Ana"), AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Mum.Merging.HandleAsync();

        Assert.Equal(0, merged.Machines);
        Assert.True(merged.Outcome.ChangedNothing);
    }

    [Fact]
    public async Task ProposalsAreNotPublished()
    {
        // The other machine will make its own, and better ones, from the
        // confirmations it has just been given.
        Both(@"2019\a.jpg");
        Mum.Answer(
            Mum.Db.Faces.Single(), Mum.Person("Ana"), AssignmentSource.Proposed, Monday);

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Dad.Merging.HandleAsync();

        // The person travels, because somebody typed that name. The guess does not.
        Assert.Equal(1, merged.Outcome.PeopleGained);
        Assert.Empty(Dad.Db.FaceAssignments);
    }

    [Fact]
    public async Task ADeletedPersonDoesNotComeBack()
    {
        Both(@"2019\a.jpg");
        Person ana = Mum.Person("Ana");
        Mum.Answer(Mum.Db.Faces.Single(), ana, AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        // Dad deletes her, publishes, and Mum takes the deletion.
        Dad.Db.FaceAssignments.ExecuteDelete();
        Dad.Db.People.IgnoreQueryFilters()
            .ExecuteUpdate(s => s.SetProperty(p => p.DeletedUtc, Monday.AddDays(1)));
        Dad.Db.ChangeTracker.Clear();

        await Dad.Publishing.HandleAsync();
        MergeResult merged = await Mum.Merging.HandleAsync();

        Assert.Equal(1, merged.Outcome.PeopleDeleted);
        Assert.Empty(Mum.Db.People);
        Assert.Empty(Mum.Db.FaceAssignments);
        Assert.NotNull(Mum.Db.People.IgnoreQueryFilters().Single().DeletedUtc);
    }

    [Fact]
    public async Task AMachineIsRememberedWithWhenItLastShared()
    {
        // The honest form of "is everybody in step?" for a mechanism where being
        // online is beside the point.
        Both(@"2019\a.jpg");
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Peer peer = Dad.Db.Peers.Single();
        Assert.Equal(Mum.MachineId, peer.MachineId);
        Assert.Equal("Mum's laptop", peer.Name);
        Assert.NotNull(peer.LastMergedUtc);
    }

    [Fact]
    public async Task AFileThatCannotBeReadIsNamedRatherThanSwallowed()
    {
        Both(@"2019\a.jpg");
        await Mum.Publishing.HandleAsync();

        string answers = Path.Combine(_house.SharedFolder, SharedFolderExchange.AnswersFolder);
        await File.WriteAllTextAsync(
            Path.Combine(answers, $"{Guid.NewGuid():D}.json.gz"), "not a gzip file");

        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.True(merged.Merged);
        Assert.Single(merged.Unreadable);
    }

    // ------------------------------------------------------------------ setup

    /// <summary>One photograph, indexed and detected on both machines.</summary>
    private void Both(string relativePath)
    {
        Face(Mum, Photo(Mum, relativePath));
        Face(Dad, Photo(Dad, relativePath));
    }

    private static Asset Photo(Library library, string relativePath) =>
        library.Photo(relativePath);

    private static Face Face(Library library, Asset asset) => library.Face(asset, Head);

    public void Dispose() => _house.Dispose();
}

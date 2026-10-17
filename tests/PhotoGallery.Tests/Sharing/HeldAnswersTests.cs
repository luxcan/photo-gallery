using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Answers that arrived before their photographs did, and the scan phase that
/// finally lands them.
/// </summary>
/// <remarks>
/// The half of holding an answer that makes holding it worth anything. Every
/// test here shares before it scans - which is the order somebody actually does
/// it in, and the one that used to lose an evening's work.
/// </remarks>
public sealed class HeldAnswersTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tuesday = Monday.AddDays(1);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task AnAnswerThatWaitedIsAppliedByTheNextScan()
    {
        // Shared before scanned, which is the order that used to lose the work.
        await MumNames(@"2026 Phone Dump\b.jpg");
        await Dad.Merging.HandleAsync();

        Assert.Single(Dad.Db.HeldDecisions);
        Assert.Empty(Dad.Db.FaceAssignments);

        DadScans(@"2026 Phone Dump\b.jpg");
        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Equal(1, swept.Applied);
        Assert.Equal(0, swept.Waiting);

        FaceAssignment landed = Dad.Db.FaceAssignments.Single();
        Assert.Equal(AssignmentSource.Confirmed, landed.Source);

        // The moment and the machine are the ones from the answer, not this
        // machine and not now. An answer that lost either on the way in would be
        // republished as this library's own.
        Assert.Equal(Monday, landed.DecidedUtc);
        Assert.Equal(Mum.MachineId, landed.DecidedBy);
    }

    [Fact]
    public async Task AnAnswerIsAppliedInTheScanThatFindsTheFaceNotTheOneAfter()
    {
        // Why the phase runs after the faces rather than after the indexing. A
        // photograph indexed a moment ago has no faces yet, so a sweep placed
        // any earlier would hold every name for another whole scan.
        await MumNames(@"2026 Phone Dump\b.jpg");
        await Dad.Merging.HandleAsync();

        // Indexed, but not yet looked at for faces: this is the state the crawl
        // leaves behind, and the answer is right to keep waiting in it.
        Asset photo = Dad.Photo(@"2026 Phone Dump\b.jpg");
        HeldResult tooEarly = await Dad.Waiting.HandleAsync();

        Assert.Equal(0, tooEarly.Applied);
        Assert.Equal(1, tooEarly.Waiting);
        Assert.Single(Dad.Db.HeldDecisions);

        // The face pass runs, and the same scan's sweep lands it.
        Dad.Face(photo, Head);
        HeldResult now = await Dad.Waiting.HandleAsync();

        Assert.Equal(1, now.Applied);
        Assert.Single(Dad.Db.FaceAssignments);
    }

    [Fact]
    public async Task WhatStillWaitsIsKeptAndWhatLandedIsForgotten()
    {
        // A held row is the only record of an answer somebody spent an evening
        // making, so the two have to be told apart exactly.
        await MumNames(@"2026 Phone Dump\b.jpg", @"2026 Phone Dump\c.jpg");
        await Dad.Merging.HandleAsync();

        Assert.Equal(2, Dad.Db.HeldDecisions.Count());

        DadScans(@"2026 Phone Dump\b.jpg");
        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Equal(1, swept.Applied);
        Assert.Equal(1, swept.Waiting);

        HeldDecision kept = Dad.Db.HeldDecisions.Single();
        Assert.Equal(@"2026 Phone Dump\c.jpg", kept.RelativePath);
        Assert.Equal(Mum.MachineId, kept.FromMachine);
    }

    [Fact]
    public async Task SweepingTwiceChangesNothingTheSecondTime()
    {
        await MumNames(@"2026 Phone Dump\b.jpg");
        await Dad.Merging.HandleAsync();

        DadScans(@"2026 Phone Dump\b.jpg");
        await Dad.Waiting.HandleAsync();

        HeldResult again = await Dad.Waiting.HandleAsync();

        Assert.Equal(0, again.Applied);
        Assert.Equal(0, again.Waiting);
        Assert.Single(Dad.Db.FaceAssignments);
        Assert.Empty(Dad.Db.HeldDecisions);
    }

    [Fact]
    public async Task ALibraryWithNothingWaitingIsNotCharged()
    {
        // Why this can sit inside the core action rather than behind a button:
        // on a library nobody shares with it is one count and nothing else.
        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Equal(HeldResult.Nothing, swept);
    }

    [Fact]
    public async Task AnAnswerSurvivesBeingWrittenDown()
    {
        // A key is a struct with a compact text form and no parameterless
        // constructor, so the plain serialiser writes something nothing can read
        // back. Asserted through the row itself, because a held answer parked in
        // a shape that cannot be parsed is one silently lost - which is the one
        // thing holding it exists to prevent.
        await MumNames(@"2026 Phone Dump\b.jpg");
        await Dad.Merging.HandleAsync();

        Dad.Db.ChangeTracker.Clear();
        HeldAnswers read = await Dad.Decisions.WaitingAsync();

        FaceAnswer answer = Assert.Single(read.Answers);
        Assert.Equal(@"2026 Phone Dump\b.jpg", answer.Face.Photo.RelativePath);
        Assert.Equal(Dad.SharedSourceId, answer.Face.Photo.SharedSourceId);
        Assert.Equal(Head, answer.Face.Bounds);
        Assert.Equal(AssignmentSource.Confirmed, answer.Source);
        Assert.Equal(Monday, answer.DecidedUtc);
        Assert.Equal(Mum.MachineId, answer.DecidedBy);
    }

    [Fact]
    public async Task TwoAnswersAboutOneFaceBothWait()
    {
        // One face carries one name but several answers - refused as the elder
        // child, confirmed as the younger. A held row keyed on the box alone
        // would keep the last of them and lose the rest.
        Asset photo = Mum.Photo(@"2026 Phone Dump\b.jpg");
        Face face = Mum.Face(photo, Head);

        Person ana = Mum.Person("Ana");
        Person bea = Mum.Person("Bea");
        Mum.Answer(face, ana, AssignmentSource.Confirmed, Monday);
        Mum.Answer(face, bea, AssignmentSource.Rejected, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Equal(2, Dad.Db.HeldDecisions.Count());

        DadScans(@"2026 Phone Dump\b.jpg");
        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Equal(2, swept.Applied);

        List<FaceAssignment> landed = [.. Dad.Db.FaceAssignments];
        Assert.Equal(2, landed.Count);
        Assert.Single(landed, a => a.Source == AssignmentSource.Confirmed);
        Assert.Single(landed, a => a.Source == AssignmentSource.Rejected);
    }

    [Fact]
    public async Task AMarkAndANameAboutOneFaceAreSettledWhenTheyLand()
    {
        // Both wait, because while they wait they are two different answers.
        // Which one stands is decided where every other disagreement is: at the
        // moment they land, by which was decided later.
        Asset photo = Mum.Photo(@"2026 Phone Dump\b.jpg");
        Face face = Mum.Face(photo, Head);
        Mum.Answer(face, Mum.Person("Ana"), AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        // Mum changes her mind the next day: that face is nobody after all.
        Mum.Db.FaceAssignments.ExecuteDelete();
        Mum.Db.Faces.ExecuteUpdate(s => s
            .SetProperty(f => f.IgnoredUtc, Monday.AddDays(1))
            .SetProperty(f => f.IgnoredBy, Mum.MachineId));
        Mum.Db.ChangeTracker.Clear();

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        DadScans(@"2026 Phone Dump\b.jpg");
        HeldResult swept = await Dad.Waiting.HandleAsync();

        // The later answer stands, and the name it beat is not applied.
        Assert.Empty(Dad.Db.FaceAssignments);
        Assert.NotNull(Dad.Db.Faces.Single().IgnoredUtc);
        Assert.Empty(Dad.Db.HeldDecisions);

        // And it is not counted as though it had been. One answer was written;
        // the other was settled by it, which is a different thing to report.
        Assert.Equal(1, swept.Applied);
        Assert.Equal(1, swept.Settled);
    }

    /// <summary>
    /// A name for somebody deleted here while it waited is let go, not written
    /// onto what is left of them.
    /// </summary>
    /// <remarks>
    /// The deletion wins, as it does for the person and for an album. Written,
    /// the name landed on the tombstone and was counted as applied - an answer
    /// about nobody anybody can see, reported as work arriving.
    /// </remarks>
    [Fact]
    public async Task ANameForSomebodyDeletedWhileItWaitedIsSettledNotWritten()
    {
        await MumNames(@"2026 Phone Dump\b.jpg");
        await Dad.Merging.HandleAsync();

        Dad.Db.People.IgnoreQueryFilters()
            .ExecuteUpdate(s => s.SetProperty(p => p.DeletedUtc, Tuesday));
        Dad.Db.ChangeTracker.Clear();

        DadScans(@"2026 Phone Dump\b.jpg");
        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Empty(Dad.Db.FaceAssignments);
        Assert.Empty(Dad.Db.HeldDecisions);
        Assert.Equal(0, swept.Applied);
        Assert.Equal(1, swept.Settled);
    }

    /// <summary>
    /// A photograph is not counted into an album this library threw away while
    /// the answer waited.
    /// </summary>
    /// <remarks>
    /// Let go, because a tombstone is final and nothing may join it - but let go
    /// as settled, not as applied. The count is the only thing anybody reads
    /// about a sweep, and one that said "applied" about a photograph that went
    /// nowhere is the count that cannot be trusted about the ones that did.
    /// </remarks>
    [Fact]
    public async Task AMoveIntoAnAlbumDeletedWhileItWaitedIsNotCountedAsApplied()
    {
        Album hers = Mum.Album("Genting", Monday);
        Mum.Put(hers, Mum.Prepared(@"2019\a.jpg", "aaa.jpg"), Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Single(Dad.Db.HeldDecisions, h => h.Kind == HeldDecisionKind.SharedAlbumMembership);

        Dad.Remove(Dad.Db.Albums.Single(a => a.PublicId == hers.PublicId), Tuesday);
        Dad.Photo(@"2019\a.jpg");

        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Empty(Dad.Db.AlbumMembers);
        Assert.Empty(Dad.Db.HeldDecisions);
        Assert.Equal(0, swept.Applied);
        Assert.Equal(1, swept.Settled);
    }

    /// <summary>
    /// A move into an album this library has no row for waits, rather than being
    /// forgotten as though it had landed.
    /// </summary>
    /// <remarks>
    /// The one real loss in the sweep. The plan moved the photograph into an
    /// album nobody here had, the write skipped the move without a word, and the
    /// held row - for this library's own parked decisions, the only copy - was
    /// released and reported as applied. Rows like this were parked by the first
    /// release, under another machine's name for a suggestion it had kept.
    /// </remarks>
    [Fact]
    public async Task AMoveIntoAnAlbumThisLibraryHasNotGotWaitsRatherThanBeingCountedAsApplied()
    {
        await DadHoldsAMoveIntoAnAlbumHeHasNotGot(@"2019\a.jpg");
        Dad.Photo(@"2019\a.jpg");

        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Empty(Dad.Db.AlbumMembers);
        Assert.Equal(0, swept.Applied);
        Assert.Single(Dad.Db.HeldDecisions);

        // Counted as what it is: not waiting for its photograph, which is here,
        // and not settled, because nothing here has answered it.
        Assert.Equal(1, swept.Unplaced);
        Assert.Equal(0, swept.Waiting);
        Assert.Equal(0, swept.Settled);
    }

    /// <summary>
    /// And it is let go once this library holds the same answer some other way.
    /// </summary>
    /// <remarks>
    /// How a move that could not be placed comes good: a later sync places the
    /// photograph from its author's own set, under whatever this library calls
    /// that album. The held row still names the album by an identity this
    /// library has never used, so it is judged on what the answer says - when it
    /// was made and by whom - and not on which row it names.
    /// </remarks>
    [Fact]
    public async Task AMoveThatCouldNotBePlacedIsLetGoOnceThisLibraryHoldsTheSameAnswer()
    {
        await DadHoldsAMoveIntoAnAlbumHeHasNotGot(@"2019\a.jpg");
        Asset photo = Dad.Photo(@"2019\a.jpg");

        await Dad.Waiting.HandleAsync();
        Assert.Single(Dad.Db.HeldDecisions);

        // What a sync does when it places that photograph: the same moment and
        // the same author, in this library's own album.
        Album genting = Dad.Album("Genting", Monday);
        Dad.Db.AlbumMembers.Add(new AlbumMember
        {
            AlbumId = genting.Id,
            AssetId = photo.Id,
            AddedUtc = Monday,
            AddedBy = Mum.MachineId,
        });
        Dad.Db.SaveChanges();
        Dad.Db.ChangeTracker.Clear();

        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Empty(Dad.Db.HeldDecisions);
        Assert.Equal(0, swept.Applied);
        Assert.Equal(1, swept.Settled);
        Assert.Equal(genting.Id, Dad.Db.AlbumMembers.Single().AlbumId);
    }

    /// <summary>
    /// Two waiting moves for one photograph are one move.
    /// </summary>
    /// <remarks>
    /// Parked one per album, so a photograph Mum moved from one album to another
    /// while Dad did not have it waits as two rows. Only the later one is written,
    /// and only the later one is counted.
    /// </remarks>
    [Fact]
    public async Task TwoWaitingMovesForOnePhotographCountAsOne()
    {
        Album genting = Mum.Album("Genting", Monday);
        Album chingay = Mum.Album("Chingay", Monday);
        Asset photo = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Mum.Put(genting, photo, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Mum.Db.AlbumMembers.ExecuteDelete();
        Mum.Put(chingay, photo, Tuesday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Equal(2, Dad.Db.HeldDecisions.Count());

        Dad.Photo(@"2019\a.jpg");
        HeldResult swept = await Dad.Waiting.HandleAsync();

        AlbumMember landed = Assert.Single(Dad.Db.AlbumMembers);
        Assert.Equal(Dad.Db.Albums.Single(a => a.PublicId == chingay.PublicId).Id, landed.AlbumId);
        Assert.Empty(Dad.Db.HeldDecisions);
        Assert.Equal(1, swept.Applied);
        Assert.Equal(1, swept.Settled);
    }

    [Fact]
    public async Task AnswersArePickedUpByAnOrdinaryScan()
    {
        // Not a button. Nobody would think to press one, because the thing it
        // repairs happened days ago on somebody else's laptop.
        await MumNames(@"2026 Phone Dump\b.jpg");
        await Dad.Merging.HandleAsync();

        DadScans(@"2026 Phone Dump\b.jpg");

        // What RefreshLibraryHandler runs between finding faces and grouping
        // photographs into occasions.
        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Equal(1, swept.Applied);
        Assert.Single(Dad.Db.FaceAssignments);
    }

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// Mum names a face in photographs only she has, and publishes.
    /// </summary>
    private async Task MumNames(params string[] relativePaths)
    {
        Person ana = Mum.Person("Ana");

        foreach (string path in relativePaths)
        {
            Mum.Answer(
                Mum.Face(Mum.Photo(path), Head), ana, AssignmentSource.Confirmed, Monday);
        }

        await Mum.Publishing.HandleAsync();
    }

    /// <summary>Dad's crawl finds the photograph, and his face pass finds the face.</summary>
    private void DadScans(string relativePath) => Dad.Face(Dad.Photo(relativePath), Head);

    /// <summary>
    /// Parks a move from Mum into an album Dad has no row for, through the code
    /// that parks them.
    /// </summary>
    private Task DadHoldsAMoveIntoAnAlbumHeHasNotGot(string relativePath) =>
        Dad.Writing.ApplyAsync(MergePlan.Nothing with
        {
            Held = HeldAnswers.None with
            {
                Memberships =
                [
                    new SharedAlbumMembership(
                        new AssetKey(Dad.SharedSourceId, relativePath),
                        Guid.NewGuid(),
                        Monday,
                        Mum.MachineId),
                ],
            },
        });

    public void Dispose() => _house.Dispose();
}

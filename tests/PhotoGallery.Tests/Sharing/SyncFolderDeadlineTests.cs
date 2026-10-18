using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// The Sync screen and its button ask the folder under the deadline the scan
/// keeps, and say plainly when it does not answer.
/// </summary>
/// <remarks>
/// Found on the family's own library with the drive in the house switched off:
/// the screen took twenty-three seconds to say the folder could not be reached,
/// because its question had no deadline at all - while a scan asking the same
/// folder gave up after fifteen. A folder that never answers stands here for
/// that drive, as it does in the scan's own test, so nothing waits on the
/// operating system to give up on it.
/// </remarks>
public sealed class SyncFolderDeadlineTests : IDisposable
{
    private static readonly DateTime s_monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds s_head = new(10, 10, 40, 40);

    /// <summary>How long the folder is given here, so that no test waits out the real one.</summary>
    private static readonly TimeSpan s_deadline = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long the folder is given where a test wants it still being asked when
    /// it answers.
    /// </summary>
    private static readonly TimeSpan s_patient = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Far longer than <see cref="s_deadline"/>, and far shorter than a question
    /// with no deadline, which is never answered here at all.
    /// </summary>
    private static readonly TimeSpan s_bound = TimeSpan.FromSeconds(10);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task TheScreenSaysPlainlyThatAFolderThatNeverAnswersDidNot()
    {
        var folder = new SlowSyncFolder(Dad);

        SharingStatus status = await Overview(folder).HandleAsync().WaitAsync(s_bound);

        Assert.False(status.CanShare);
        Assert.Equal(_house.SharedFolder, status.Folder);
        Assert.StartsWith("That folder did not answer within ", status.Problem, StringComparison.Ordinal);
        Assert.EndsWith(_house.SharedFolder, status.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFolderThatAnswersInTimeIsUsed()
    {
        Both(@"2019\a.jpg");
        await Mum.Publishing.HandleAsync();

        var folder = new SlowSyncFolder(Dad);
        Task<SharingStatus> opening = Overview(folder, s_patient).HandleAsync();

        await folder.Asked.WaitAsync(s_bound);
        folder.Answers();

        SharingStatus status = await opening.WaitAsync(s_bound);

        Assert.True(status.CanShare);
        Assert.Empty(status.Problem);
        Assert.NotNull(Assert.Single(status.Machines).SharedUtc);
    }

    [Fact]
    public async Task SyncNowGivesUpOnAFolderThatNeverAnswersAndTakesNothing()
    {
        Both(@"2019\a.jpg");
        Mum.Answer(
            Mum.Db.Faces.Single(), Mum.Person("Ana"), AssignmentSource.Confirmed, s_monday);
        await Mum.Publishing.HandleAsync();

        var folder = new SlowSyncFolder(Dad);

        ShareResult result = await Sharing(folder).HandleAsync().WaitAsync(s_bound);

        Assert.False(result.Shared);
        Assert.StartsWith("That folder did not answer within ", result.Summary, StringComparison.Ordinal);
        Assert.Empty(Dad.Db.FaceAssignments);
    }

    /// <summary>What the Sync screen opens showing, over a folder a test controls.</summary>
    private GetSharingHandler Overview(SlowSyncFolder folder, TimeSpan? patience = null) =>
        new(Dad.Index, Dad.Decisions, folder, Probe(folder, patience));

    /// <summary>The Sync screen's one button, over a folder a test controls.</summary>
    private ShareNowHandler Sharing(SlowSyncFolder folder) =>
        new(
            new MergeDecisionsHandler(Dad.Index, Dad.Decisions, Dad.Writing, folder, Dad.Turning),
            new PublishDecisionsHandler(Dad.Index, Dad.Decisions, folder),
            Probe(folder));

    private SyncFolderProbe Probe(SlowSyncFolder folder, TimeSpan? patience = null) =>
        new(Dad.Index, folder) { Patience = patience ?? s_deadline };

    private void Both(string relativePath)
    {
        Mum.Face(Mum.Photo(relativePath), s_head);
        Dad.Face(Dad.Photo(relativePath), s_head);
    }

    public void Dispose() => _house.Dispose();
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A sharing merge applying names while a suggestion round writes beside it.
/// </summary>
/// <remarks>
/// Rounds run in the background now, so one can still be writing when the user
/// clicks Refresh or answers the "do you want to sync?" notice. The round
/// replaces every proposal with new rows; a merge that had already read those
/// rows and was about to turn one into a confirmed name would then update a row
/// that no longer exists, fail, and leave the library half-merged.
/// </remarks>
public sealed class MergeBesideARoundTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);

    private readonly TwoLibraries _house = new();

    private Library Dad => _house.Dad;

    [Fact]
    public async Task ANameArrivingOverAProposalSurvivesARoundWritingMeanwhile()
    {
        Face face = Dad.Face(Dad.Photo(@"2019\a.jpg"), Head);
        Person ana = Dad.Person("Ana");
        Dad.Answer(face, ana, AssignmentSource.Proposed, Monday);

        MergePlan plan = MergePlan.Nothing with
        {
            Answers =
            [
                new FaceAnswer(
                    new FaceKey(Dad.KeyOf(Dad.Db.Assets.Single()), Head),
                    ana.PublicId,
                    AssignmentSource.Confirmed,
                    Monday.AddDays(1),
                    Guid.NewGuid()),
            ],
        };

        // The round's write, on a connection of its own, let go at the worst
        // moment: after the merge has read the face's proposal and just before
        // it saves the confirmed name over it. It would offer the face to Ana
        // again from what it read before the merge.
        Task? roundWrite = null;
        var atTheSave = new BeforeTheFirstUpdate(() =>
        {
            roundWrite = Task.Run(async () =>
            {
                await using GalleryDbContext round = Open();
                await new SqlitePeopleRepository(round).ReplaceProposalsAsync(
                    new Dictionary<int, IReadOnlyList<ScoredFace>>
                    {
                        [ana.Id] = [new ScoredFace(face.Id, 0.9f)],
                    });
            });

            // Long enough for the round to commit if nothing holds it back.
            return Task.WhenAny(roundWrite, Task.Delay(TimeSpan.FromSeconds(1)));
        });

        await using (GalleryDbContext merging = Open(atTheSave))
        {
            MergeOutcome outcome = await new SqliteDecisionRepository(merging).ApplyAsync(plan);
            Assert.Equal(1, outcome.NamesReplaced);
        }

        Assert.NotNull(roundWrite);
        await roundWrite.WaitAsync(TimeSpan.FromSeconds(30));

        await using GalleryDbContext check = Open();
        FaceAssignment only = await check.FaceAssignments.SingleAsync(a => a.FaceId == face.Id);
        Assert.Equal(AssignmentSource.Confirmed, only.Source);
        Assert.Equal(ana.Id, only.PersonId);
    }

    private GalleryDbContext Open(IInterceptor? interceptor = null)
    {
        DbContextOptionsBuilder<GalleryDbContext> options = new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Dad.Root, "index.db")}");

        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new GalleryDbContext(options.Options);
    }

    public void Dispose() => _house.Dispose();

    /// <summary>
    /// Runs something once, just before the first save that changes a face's
    /// existing assignment - the merge's save of the names it read.
    /// </summary>
    private sealed class BeforeTheFirstUpdate(Func<Task> meanwhile) : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_done
                && eventData.Context is { } context
                && context.ChangeTracker.Entries<FaceAssignment>()
                    .Any(entry => entry.State == EntityState.Modified))
            {
                _done = true;
                await meanwhile();
            }

            return result;
        }
    }
}

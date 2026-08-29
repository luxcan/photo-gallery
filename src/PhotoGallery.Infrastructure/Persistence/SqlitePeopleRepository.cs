using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IPeopleRepository"/>
public sealed class SqlitePeopleRepository : IPeopleRepository
{
    private readonly GalleryDbContext _db;

    public SqlitePeopleRepository(GalleryDbContext db) => _db = db;

    public async Task<int> EnsurePersonAsync(
        string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string name = displayName.Trim();

        Person? existing = await _db.Set<Person>()
            .FirstOrDefaultAsync(person => person.DisplayName == name, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Id;
        }

        var person = new Person { DisplayName = name };
        _db.Set<Person>().Add(person);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        return person.Id;
    }

    public async Task RenamePersonAsync(
        int personId, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        DateTime now = DateTime.UtcNow;

        await _db.Set<Person>()
            .Where(person => person.Id == personId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.DisplayName, displayName.Trim())
                    .SetProperty(p => p.UpdatedUtc, now),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetBirthYearAsync(
        int personId, int? birthYear, CancellationToken cancellationToken = default)
    {
        // Checked here as well as in the view model, so a year that could only be
        // a slip cannot reach the column from a caller that forgot to look.
        if (birthYear is int year && !PersonAge.IsPlausible(year, DateTime.Today))
        {
            throw new ArgumentOutOfRangeException(
                nameof(birthYear),
                birthYear,
                $"A year of birth is between {PersonAge.EarliestYear} and this year.");
        }

        await _db.Set<Person>()
            .Where(person => person.Id == personId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.BirthYear, birthYear),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AssignAsync(
        int personId,
        IReadOnlyList<ScoredFace> faces,
        AssignmentSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(faces);

        if (faces.Count == 0)
        {
            return;
        }

        ScoredFace[] distinct = [.. faces.DistinctBy(face => face.FaceId)];
        int[] faceIds = [.. distinct.Select(face => face.FaceId)];

        // One transaction over the clearing and the writing. Each ExecuteDelete
        // commits on its own, so without this a failure between them - a full
        // disk, the share going away mid-statement - would leave the person's
        // previous answers deleted and nothing put in their place. What the user
        // said about a face is not something to lose while recording what they
        // said about it next.
        await using IDbContextTransaction transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Chunked: SQLite caps parameters per statement, and confirming a large
        // group is exactly when that limit is met.
        foreach (int[] chunk in faceIds.Chunk(400))
        {
            // This person's previous answer about these faces, cleared so that
            // saying something new replaces it rather than colliding with it -
            // the index holds one answer per face and person.
            //
            // Only this person's. Clearing every row would take other people's
            // rejections with it, and a rejection is a promise not to ask again:
            // a face refused as one child and then offered to the other must
            // still be refused for the first, or the next round asks all over.
            await _db.FaceAssignments
                .Where(assignment => assignment.PersonId == personId
                                  && chunk.Contains(assignment.FaceId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            // Saying who somebody is settles the face for everyone, so the
            // guesses other people were holding are withdrawn. Their rejections
            // are not - those were answers, not guesses.
            if (source == AssignmentSource.Confirmed)
            {
                await _db.FaceAssignments
                    .Where(assignment => assignment.Source == AssignmentSource.Proposed
                                      && chunk.Contains(assignment.FaceId))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // One moment for the whole batch, not one per row. Confirming a
        // screenful is a single answer, and stamping each face as the loop
        // reaches it would order them by nothing anybody did.
        DateTime decided = DateTime.UtcNow;

        _db.FaceAssignments.AddRange(distinct.Select(face => new FaceAssignment
        {
            FaceId = face.FaceId,
            PersonId = personId,
            Source = source,
            Score = face.Score,
            DecidedUtc = decided,
        }));

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    public async Task ClearProposalsAsync(int personId, CancellationToken cancellationToken = default) =>
        await _db.FaceAssignments
            .Where(assignment => assignment.PersonId == personId
                              && assignment.Source == AssignmentSource.Proposed)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task UnassignAsync(
        IReadOnlyList<int> faceIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(faceIds);

        // Chunked: SQLite caps parameters per statement, and confirming a large
        // group is exactly when that limit is met.
        foreach (int[] chunk in faceIds.Distinct().Chunk(400))
        {
            await _db.FaceAssignments
                .Where(assignment => chunk.Contains(assignment.FaceId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task SetIgnoredAsync(
        IReadOnlyList<int> faceIds,
        bool ignored,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(faceIds);

        if (faceIds.Count == 0)
        {
            return;
        }

        DateTime? mark = ignored ? DateTime.UtcNow : null;

        // Anything said about a face that is nobody was said about the wrong
        // thing, so it goes with it - including proposals, which is the point.
        if (ignored)
        {
            await UnassignAsync(faceIds, cancellationToken).ConfigureAwait(false);
        }

        foreach (int[] chunk in faceIds.Distinct().Chunk(400))
        {
            await _db.Faces
                .Where(face => chunk.Contains(face.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(face => face.IgnoredUtc, mark),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task ReplaceErasAsync(
        int personId,
        IReadOnlyList<PersonEra> eras,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eras);

        await _db.Set<PersonEra>()
            .Where(era => era.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (eras.Count == 0)
        {
            return;
        }

        foreach (PersonEra era in eras)
        {
            era.PersonId = personId;
        }

        _db.Set<PersonEra>().AddRange(eras);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    public async Task RemovePersonAsync(int personId, CancellationToken cancellationToken = default)
    {
        // Everything they were is taken - their faces go back to being nobody in
        // particular - but the row stays as a tombstone. It is the only record
        // that this person was deleted rather than never known, and without it
        // the next merge from any machine that still holds them puts them back,
        // and then propagates. It is never expired for the same reason.
        await _db.FaceAssignments
            .Where(assignment => assignment.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await _db.Set<PersonEra>()
            .Where(era => era.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;

        await _db.Set<Person>()
            .Where(person => person.Id == personId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.DeletedUtc, now),
                cancellationToken)
            .ConfigureAwait(false);

        _db.ChangeTracker.Clear();
    }
}

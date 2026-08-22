using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IPeopleReader"/>
public sealed class SqlitePeopleReader : IPeopleReader
{
    private readonly GalleryDbContext _db;

    public SqlitePeopleReader(GalleryDbContext db) => _db = db;

    public async Task<IReadOnlyList<FaceRecord>> GetFacesAsync(
        bool includeEmbeddings, CancellationToken cancellationToken = default)
    {
        var rows = await _db.Faces
            .AsNoTracking()
            .Join(
                InLibrary(),
                face => face.AssetId,
                asset => asset.Id,
                (face, asset) => new
                {
                    face.Id,
                    face.AssetId,
                    face.Bounds,
                    face.DetectScore,
                    face.IgnoredUtc,
                    asset.ThumbnailName,
                    asset.TakenUtc,
                    asset.ModifiedUtc,
                    asset.CreatedUtc,
                    asset.RelativePath,
                    asset.PhotoSourceId,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Asked for separately so that not asking is possible. Read as part of
        // the row above, the vectors would arrive whether or not the caller had
        // any use for them - and they are fifty times the weight of everything
        // else on a face.
        Dictionary<int, FaceEmbedding> vectors = includeEmbeddings
            ? await _db.Faces
                .AsNoTracking()
                .Join(
                    InLibrary(),
                    face => face.AssetId,
                    asset => asset.Id,
                    (face, asset) => new { face.Id, face.Embedding })
                .ToDictionaryAsync(row => row.Id, row => row.Embedding, cancellationToken)
                .ConfigureAwait(false)
            : [];

        var claims = await _db.FaceAssignments
            .AsNoTracking()
            .Where(assignment => assignment.Source != AssignmentSource.Rejected)
            .Select(assignment => new
            {
                assignment.FaceId, assignment.PersonId, assignment.Source,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A confirmation outranks a proposal: the same face can be proposed to
        // one person and confirmed as another, and what the user said wins.
        var bestClaim = claims
            .GroupBy(claim => claim.FaceId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(claim => claim.Source == AssignmentSource.Confirmed)
                    .First());

        // The roots, so a face can name the file it was found in.
        Dictionary<int, string> roots = await _db.Set<PhotoSource>()
            .AsNoTracking()
            .ToDictionaryAsync(source => source.Id, source => source.Path, cancellationToken)
            .ConfigureAwait(false);

        var records = new List<FaceRecord>(rows.Count);
        foreach (var row in rows)
        {
            bestClaim.TryGetValue(row.Id, out var claim);

            records.Add(new FaceRecord(
                row.Id,
                row.AssetId,
                row.ThumbnailName!,
                row.Bounds,
                row.DetectScore,
                AssetDates.BestGuess(row.TakenUtc, row.CreatedUtc, row.ModifiedUtc),
                row.RelativePath,
                roots.TryGetValue(row.PhotoSourceId, out string? root)
                    ? Path.Combine(root, row.RelativePath)
                    : string.Empty,
                vectors.TryGetValue(row.Id, out FaceEmbedding embedding)
                    ? embedding
                    : default,
                claim?.PersonId,
                claim?.Source,
                row.IgnoredUtc is not null));
        }

        return records;
    }

    public async Task<IReadOnlyList<FaceSample>> GetSamplesAsync(
        int personId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.FaceAssignments
            .AsNoTracking()
            .Where(assignment => assignment.PersonId == personId
                              && assignment.Source == AssignmentSource.Confirmed)
            .Join(
                _db.Faces.AsNoTracking(),
                assignment => assignment.FaceId,
                face => face.Id,
                (assignment, face) => face)
            .Join(
                InLibrary(),
                face => face.AssetId,
                asset => asset.Id,
                (face, asset) => new
                {
                    face.Id,
                    face.Embedding,
                    asset.TakenUtc,
                    asset.ModifiedUtc,
                    asset.CreatedUtc,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new FaceSample(
                row.Id,
                AssetDates.BestGuess(row.TakenUtc, row.CreatedUtc, row.ModifiedUtc),
                row.Embedding)),
        ];
    }

    public async Task<IReadOnlyList<FaceOnPhoto>> GetFacesOnAsync(
        int assetId, CancellationToken cancellationToken = default)
    {
        List<Face> faces = await _db.Faces
            .AsNoTracking()
            .Where(face => face.AssetId == assetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (faces.Count == 0)
        {
            return [];
        }

        int[] ids = [.. faces.Select(face => face.Id)];

        var claims = await _db.FaceAssignments
            .AsNoTracking()
            .Where(assignment => ids.Contains(assignment.FaceId)
                              && assignment.Source != AssignmentSource.Rejected)
            .Join(
                _db.Set<Person>(),
                assignment => assignment.PersonId,
                person => person.Id,
                (assignment, person) => new
                {
                    assignment.FaceId, assignment.PersonId, person.DisplayName, assignment.Source,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A confirmation outranks a proposal for the same face.
        var best = claims
            .GroupBy(claim => claim.FaceId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(claim => claim.Source == AssignmentSource.Confirmed)
                    .First());

        return
        [
            .. faces
                .OrderByDescending(face => face.Bounds.Area)
                .Select(face =>
                {
                    best.TryGetValue(face.Id, out var claim);
                    return new FaceOnPhoto(
                        face.Id,
                        face.Bounds,
                        face.DetectScore,
                        claim?.PersonId,
                        claim?.DisplayName,
                        claim?.Source,
                        face.IgnoredUtc is not null);
                }),
        ];
    }

    public async Task<IReadOnlyList<Person>> GetPeopleAsync(
        CancellationToken cancellationToken = default) =>
        await _db.Set<Person>()
            .AsNoTracking()
            .Include(person => person.Eras)
            .OrderBy(person => person.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
        CancellationToken cancellationToken = default)
    {
        // Two flat queries rather than a count per person. A correlated subquery
        // reads more naturally and does not translate: SQLite gets a join and a
        // grouping, or it gets nothing.
        var people = await _db.Set<Person>()
            .AsNoTracking()
            .Select(person => new { person.Id, person.DisplayName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Distinct assets rather than faces: one photograph of somebody is one
        // picture however many times they appear in it. Only the two ids are
        // projected, so no embedding is read to answer a keystroke.
        var counts = await _db.FaceAssignments
            .AsNoTracking()
            .Where(assignment => assignment.Source == AssignmentSource.Confirmed)
            .Join(
                _db.Faces,
                assignment => assignment.FaceId,
                face => face.Id,
                (assignment, face) => new { assignment.PersonId, face.AssetId })
            .Distinct()
            .GroupBy(pair => pair.PersonId)
            .Select(group => new { PersonId = group.Key, Photos = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, int> byPerson = counts.ToDictionary(row => row.PersonId, row => row.Photos);

        return
        [
            .. people
                .Select(person => new PersonDirectoryEntry(
                    person.Id,
                    person.DisplayName,
                    byPerson.TryGetValue(person.Id, out int photos) ? photos : 0))
                .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    public async Task<IReadOnlyList<FaceRejection>> GetRejectionsAsync(
        CancellationToken cancellationToken = default) =>
        await _db.FaceAssignments
            .AsNoTracking()
            .Where(assignment => assignment.Source == AssignmentSource.Rejected)
            .Select(assignment => new FaceRejection(assignment.FaceId, assignment.PersonId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The photographs a face may be found in: still in the library, and with a
    /// rendition the detector could have looked at.
    /// </summary>
    /// <remarks>
    /// A copy set aside as redundant has been moved out of the library, so its
    /// faces are not faces in the library either - offering one to be named would
    /// ask about a picture the user has already dealt with, and the path on the
    /// record would point at where the file used to be. Every other reader
    /// filters these out; this one has to as well or the people screens disagree
    /// with the gallery about what the library contains.
    /// </remarks>
    private IQueryable<Asset> InLibrary() =>
        _db.Assets
            .AsNoTracking()
            .Where(asset => asset.QuarantinedUtc == null && asset.ThumbnailName != null);
}

using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Search;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IContentRepository"/>
public sealed class SqliteContentRepository : IContentRepository
{
    private readonly GalleryDbContext _db;

    public SqliteContentRepository(GalleryDbContext db) => _db = db;

    public async Task SaveAsync(
        IReadOnlyList<ContentScanUpdate> updates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0)
        {
            return;
        }

        int[] assetIds = [.. updates.SelectMany(update => update.AssetIds).Distinct()];

        // Cleared first so that re-describing a picture replaces its answer
        // rather than colliding with it. The row is keyed on the asset, and a
        // second insert for one would fail rather than overwrite.
        foreach (int[] chunk in assetIds.Chunk(400))
        {
            await _db.PhotoContent
                .Where(content => chunk.Contains(content.AssetId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (ContentScanUpdate update in updates)
        {
            foreach (int assetId in update.AssetIds)
            {
                _db.PhotoContent.Add(new PhotoContent
                {
                    AssetId = assetId,
                    ThumbnailName = update.ThumbnailName,
                    Vector = update.Vector,
                    IndexedUtc = update.IndexedUtc,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Released after each batch so an hour-long pass does not grow the change
        // tracker until it dominates memory.
        _db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<ContentVector>> GetVectorsAsync(
        int? personId = null,
        PlaceFilter? place = null,
        CancellationToken cancellationToken = default)
    {
        // Joined to the assets so that a copy set aside as redundant is not
        // ranked: its row survives to make restoring possible, but it is not in
        // the library and must not be an answer.
        IQueryable<PhotoContent> rows = _db.PhotoContent
            .AsNoTracking()
            .Join(
                _db.Assets.AsNoTracking().Where(asset => asset.QuarantinedUtc == null),
                content => content.AssetId,
                asset => asset.Id,
                (content, asset) => content);

        if (personId is int who)
        {
            // Confirmed only, exactly as the gallery's own person filter is: a
            // proposal is a question the user has not answered, and quietly
            // counting it as a yes would make answering it pointless.
            IQueryable<int> theirs =
                from face in _db.Faces
                join assignment in _db.FaceAssignments on face.Id equals assignment.FaceId
                where assignment.PersonId == who
                   && assignment.Source == AssignmentSource.Confirmed
                select face.AssetId;

            rows = rows.Where(content => theirs.Contains(content.AssetId));
        }

        if (place is PlaceFilter there)
        {
            // The same restriction the grid applies, so a description ranked
            // "in Hong Kong" covers exactly the photographs the grid would show
            // for Hong Kong.
            IQueryable<int> taken = PlaceRestriction
                .Apply(_db.Assets, there, _db)
                .Select(asset => asset.Id);

            rows = rows.Where(content => taken.Contains(content.AssetId));
        }

        var vectors = await rows
            .Select(content => new { content.AssetId, content.Vector })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. vectors.Select(row => new ContentVector(row.AssetId, row.Vector))];
    }

    public async Task<(int Described, int Total)> CountAsync(
        CancellationToken cancellationToken = default)
    {
        int described = await _db.PhotoContent
            .AsNoTracking()
            .Join(
                _db.Assets.AsNoTracking().Where(asset => asset.QuarantinedUtc == null),
                content => content.AssetId,
                asset => asset.Id,
                (content, asset) => content.AssetId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        int total = await _db.Assets
            .AsNoTracking()
            .CountAsync(
                asset => asset.Kind == AssetKind.Photo
                      && asset.QuarantinedUtc == null
                      && asset.ThumbnailName != null,
                cancellationToken)
            .ConfigureAwait(false);

        return (described, total);
    }
}

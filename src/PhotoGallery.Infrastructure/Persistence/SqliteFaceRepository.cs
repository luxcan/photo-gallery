using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IFaceRepository"/>
public sealed class SqliteFaceRepository : IFaceRepository
{
    private readonly GalleryDbContext _db;

    public SqliteFaceRepository(GalleryDbContext db) => _db = db;

    public async Task TurnFacesAsync(
        IReadOnlyList<int> assetIds,
        int degrees,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        // Read, move, write. The arithmetic lives on FaceBounds so that the one
        // thing that must be exactly right is pure and tested, rather than
        // spread across a database expression.
        List<Face> faces = await _db.Faces
            .Where(face => assetIds.Contains(face.AssetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (faces.Count == 0)
        {
            return;
        }

        foreach (Face face in faces)
        {
            face.Bounds = face.Bounds.TurnedClockwise(width, height, degrees);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    public async Task SaveAsync(
        IReadOnlyList<FaceScanUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0)
        {
            return;
        }

        int[] assetIds = [.. updates.SelectMany(update => update.AssetIds).Distinct()];

        // Anything recorded before describes a picture that has since changed.
        // Assignments to a person hang off these rows and go with them, which is
        // correct: a confirmation was about a face in the old image.
        foreach (int[] chunk in assetIds.Chunk(400))
        {
            await _db.Faces
                .Where(face => chunk.Contains(face.AssetId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (FaceScanUpdate update in updates)
        {
            foreach (int assetId in update.AssetIds)
            {
                _db.Faces.AddRange(update.Faces.Select(found => new Face
                {
                    AssetId = assetId,
                    Bounds = found.Bounds,
                    DetectScore = found.DetectScore,
                    Embedding = found.Embedding,
                }));
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Released after each batch so an hour-long pass does not grow the
        // change tracker until it dominates memory.
        _db.ChangeTracker.Clear();

        foreach (IGrouping<DateTime, FaceScanUpdate> sameMoment in
                 updates.GroupBy(update => update.DetectedUtc))
        {
            int[] scanned = [.. sameMoment.SelectMany(update => update.AssetIds).Distinct()];

            foreach (int[] chunk in scanned.Chunk(400))
            {
                await _db.Assets
                    .Where(asset => chunk.Contains(asset.Id))
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(a => a.FacesDetectedUtc, sameMoment.Key),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}

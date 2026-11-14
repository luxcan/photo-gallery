using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IPhotoQualityRepository"/>
public sealed class SqlitePhotoQualityRepository : IPhotoQualityRepository
{
    /// <summary>SQLite caps parameters per statement; a pass's worth of ids is chunked under it.</summary>
    private const int Chunk = 400;

    private readonly GalleryDbContext _db;

    public SqlitePhotoQualityRepository(GalleryDbContext db) => _db = db;

    public async Task<IReadOnlyList<MeasuringCandidate>> GetCandidatesAsync(
        IReadOnlyCollection<int> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        var candidates = new List<MeasuringCandidate>(assetIds.Count);
        foreach (int[] chunk in assetIds.Distinct().Chunk(Chunk))
        {
            var photos = await _db.Assets
                .AsNoTracking()
                .Where(asset => chunk.Contains(asset.Id) && asset.ThumbnailName != null)
                .Select(asset => new { asset.Id, asset.ThumbnailName })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            Dictionary<int, PreviewQuality> measured = await _db.PreviewQuality
                .AsNoTracking()
                .Where(quality => chunk.Contains(quality.AssetId))
                .ToDictionaryAsync(quality => quality.AssetId, cancellationToken)
                .ConfigureAwait(false);

            ILookup<int, FaceToMeasure> faces = (await FacesOfAsync(chunk, cancellationToken).ConfigureAwait(false))
                .ToLookup(face => face.AssetId, face => face.Face);

            candidates.AddRange(photos.Select(photo => new MeasuringCandidate(
                photo.Id,
                photo.ThumbnailName!,
                measured.GetValueOrDefault(photo.Id)?.ThumbnailName,
                measured.GetValueOrDefault(photo.Id)?.PreviewWrittenUtc,
                [.. faces[photo.Id]])));
        }

        return candidates;
    }

    public async Task SaveAsync(
        PhotoMeasurement measurement, DateTime measuredUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        PreviewQuality? row = await _db.PreviewQuality
            .FirstOrDefaultAsync(quality => quality.AssetId == measurement.AssetId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new PreviewQuality { AssetId = measurement.AssetId, ThumbnailName = measurement.ThumbnailName };
            _db.PreviewQuality.Add(row);
        }

        row.ThumbnailName = measurement.ThumbnailName;
        row.PreviewWrittenUtc = measurement.PreviewWrittenUtc;
        row.Width = measurement.Preview.Width;
        row.Height = measurement.Preview.Height;
        row.Sharpness = measurement.Preview.Sharpness;
        row.ClippedHigh = measurement.Preview.ClippedHigh;
        row.ClippedLow = measurement.Preview.ClippedLow;
        row.MeasuredUtc = measuredUtc;

        int[] faceIds = [.. measurement.Faces.Select(face => face.FaceId)];
        Dictionary<int, FaceEyes> existing = await _db.FaceEyes
            .Where(eyes => faceIds.Contains(eyes.FaceId))
            .ToDictionaryAsync(eyes => eyes.FaceId, cancellationToken)
            .ConfigureAwait(false);

        // Only faces still in the library: the face pass may have replaced them
        // between reading the candidates and getting here, and a row for a face
        // that has gone would fail its foreign key and take the photograph's
        // whole measurement with it.
        HashSet<int> stillThere = [.. await _db.Faces
            .Where(face => faceIds.Contains(face.Id))
            .Select(face => face.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)];

        foreach (FaceMeasurement face in measurement.Faces.Where(face => stillThere.Contains(face.FaceId)))
        {
            if (!existing.TryGetValue(face.FaceId, out FaceEyes? eyes))
            {
                eyes = new FaceEyes { FaceId = face.FaceId };
                _db.FaceEyes.Add(eyes);
            }

            eyes.State = face.State;
            eyes.Openness = face.Openness;
            eyes.Sharpness = face.Sharpness;
            eyes.MeasuredUtc = measuredUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyDictionary<int, CopyQuality>> GetQualitiesAsync(
        IReadOnlyCollection<int> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        var qualities = new Dictionary<int, CopyQuality>(assetIds.Count);
        foreach (int[] chunk in assetIds.Distinct().Chunk(Chunk))
        {
            var assets = await _db.Assets
                .AsNoTracking()
                .Where(asset => chunk.Contains(asset.Id))
                .Select(asset => new
                {
                    asset.Id,
                    asset.Width,
                    asset.Height,
                    asset.TakenUtc,
                    asset.ThumbnailName,
                    HasCoordinates = asset.Latitude != null && asset.Longitude != null,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            Dictionary<int, PreviewQuality> previews = await _db.PreviewQuality
                .AsNoTracking()
                .Where(quality => chunk.Contains(quality.AssetId))
                .ToDictionaryAsync(quality => quality.AssetId, cancellationToken)
                .ConfigureAwait(false);

            ILookup<int, CopyFace> known = (await KnownFacesOfAsync(chunk, cancellationToken).ConfigureAwait(false))
                .ToLookup(face => face.AssetId, face => face.Face);

            foreach (var asset in assets)
            {
                // A reading of a rendition the photograph no longer draws with
                // describes a picture that is not there; until the next pass
                // measures the new one, it has none.
                PreviewQuality? preview = previews.GetValueOrDefault(asset.Id) is { } measured
                                          && measured.ThumbnailName == asset.ThumbnailName
                    ? measured
                    : null;
                qualities[asset.Id] = new CopyQuality(
                    asset.Id,
                    asset.Width is int width && asset.Height is int height ? (long)width * height : 0L,
                    asset.TakenUtc,
                    asset.HasCoordinates,
                    preview?.Sharpness,
                    preview?.ClippedHigh,
                    preview?.ClippedLow,
                    [.. known[asset.Id]],
                    preview?.Width ?? 0,
                    preview?.Height ?? 0);
            }
        }

        return qualities;
    }

    /// <summary>Every face on these photographs that is not ignored, and whether it is measured.</summary>
    private async Task<List<(int AssetId, FaceToMeasure Face)>> FacesOfAsync(
        int[] assetIds, CancellationToken cancellationToken)
    {
        var faces = await _db.Faces
            .AsNoTracking()
            .Where(face => assetIds.Contains(face.AssetId) && face.IgnoredUtc == null)
            .Select(face => new
            {
                face.Id,
                face.AssetId,
                face.Bounds,
                Measured = _db.FaceEyes.Any(eyes => eyes.FaceId == face.Id),
                EyesRead = _db.FaceEyes.Any(eyes => eyes.FaceId == face.Id && eyes.State != null),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. faces.Select(face => (face.AssetId, new FaceToMeasure(face.Id, face.Bounds, face.Measured, face.EyesRead)))];
    }

    /// <summary>
    /// The known people's faces on these photographs, with what was measured on
    /// each.
    /// </summary>
    /// <remarks>
    /// Confirmed or proposed to a named person still in the library, on a face
    /// that is not ignored. Should two live rows ever name one face, the
    /// confirmed one is the answer a person gave and wins.
    /// </remarks>
    private async Task<List<(int AssetId, CopyFace Face)>> KnownFacesOfAsync(
        int[] assetIds, CancellationToken cancellationToken)
    {
        var rows = await (
                from face in _db.Faces.AsNoTracking()
                where assetIds.Contains(face.AssetId) && face.IgnoredUtc == null
                join assignment in _db.FaceAssignments.AsNoTracking() on face.Id equals assignment.FaceId
                where assignment.Source == AssignmentSource.Confirmed
                      || assignment.Source == AssignmentSource.Proposed
                join person in _db.People.AsNoTracking() on assignment.PersonId equals person.Id
                where person.DeletedUtc == null && person.DisplayName != ""
                join measured in _db.FaceEyes.AsNoTracking() on face.Id equals measured.FaceId into eyes
                from eye in eyes.DefaultIfEmpty()
                select new
                {
                    face.Id,
                    face.AssetId,
                    face.Bounds,
                    assignment.PersonId,
                    person.DisplayName,
                    assignment.Source,
                    State = eye == null ? null : eye.State,
                    Sharpness = eye == null ? null : eye.Sharpness,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(row => !string.IsNullOrWhiteSpace(row.DisplayName))
                .GroupBy(row => row.Id)
                .Select(group => group.OrderByDescending(row => row.Source == AssignmentSource.Confirmed).First())
                .Select(row => (row.AssetId, new CopyFace(
                    row.Id, row.PersonId, row.DisplayName, row.Bounds, row.State, row.Sharpness))),
        ];
    }
}

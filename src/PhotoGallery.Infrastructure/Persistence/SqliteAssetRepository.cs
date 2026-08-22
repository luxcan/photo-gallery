using System.IO;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Places;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IAssetRepository"/>
public sealed class SqliteAssetRepository : IAssetRepository
{
    private readonly GalleryDbContext _db;

    public SqliteAssetRepository(GalleryDbContext db) => _db = db;

    public async Task<Dictionary<string, AssetSignature>> GetSignaturesAsync(
        int photoSourceId,
        CancellationToken cancellationToken = default)
    {
        // Projected, not tracked: a scan of 17,000 files only needs three
        // columns, and materialising whole entities would cost far more memory
        // and change-tracking work than the comparison is worth.
        var rows = await _db.Assets
            .AsNoTracking()
            .Where(a => a.PhotoSourceId == photoSourceId)
            .Select(a => new
            {
                a.Id, a.RelativePath, a.Length, a.ModifiedUtc, a.CreatedUtc,
                a.ThumbnailName, a.QuarantinedUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var signatures = new Dictionary<string, AssetSignature>(
            rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            signatures[row.RelativePath] = new AssetSignature(
                row.Id, row.Length, row.ModifiedUtc, row.CreatedUtc, row.ThumbnailName,
                row.QuarantinedUtc is not null);
        }

        return signatures;
    }

    public async Task AddRangeAsync(
        IReadOnlyList<Asset> assets,
        CancellationToken cancellationToken = default)
    {
        if (assets.Count == 0)
        {
            return;
        }

        _db.Assets.AddRange(assets);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Entities are released after each batch so a long scan does not grow
        // the change tracker until it dominates memory.
        _db.ChangeTracker.Clear();
    }

    public async Task UpdateRangeAsync(
        IReadOnlyList<Asset> assets,
        CancellationToken cancellationToken = default)
    {
        if (assets.Count == 0)
        {
            return;
        }

        // What the picture is of belonged to the picture that was there before.
        // Removed rather than cleared, because the row's existence is what tells
        // the indexing pass this photograph has been read - a row kept with a
        // stale vector would never be looked at again.
        int[] changed = [.. assets.Select(asset => asset.Id)];
        foreach (int[] chunk in changed.Chunk(400))
        {
            await _db.PhotoContent
                .Where(content => chunk.Contains(content.AssetId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (Asset asset in assets)
        {
            // Only the facts the scan re-established are written. Everything
            // derived from the file's contents is cleared so the later passes
            // redo it - including the dimensions and the capture date, which a
            // replaced file can change just as easily as its pixels.
            //
            // IndexedUtc is deliberately absent: the file changed, not when it
            // joined the library.
            await _db.Assets
                .Where(a => a.Id == asset.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(a => a.Length, asset.Length)
                        .SetProperty(a => a.ModifiedUtc, asset.ModifiedUtc)
                        .SetProperty(a => a.CreatedUtc, asset.CreatedUtc)
                        .SetProperty(a => a.Kind, asset.Kind)
                        .SetProperty(a => a.ContentHash, (string?)null)
                        .SetProperty(a => a.PerceptualHash, (PerceptualHash?)null)
                        .SetProperty(a => a.ThumbnailName, (string?)null)
                        .SetProperty(a => a.TakenUtc, (DateTime?)null)
                        .SetProperty(a => a.Width, (int?)null)
                        .SetProperty(a => a.Height, (int?)null)

                        // Where it was taken, and the place that was resolved
                        // from it. This is the one and only thing that clears a
                        // place: the bytes have changed, so the coordinates
                        // recorded describe a photograph that is no longer here
                        // and the name derived from them is no better. Clearing
                        // the marker with them is what has the locating pass
                        // offer the row again; leaving it set would freeze the
                        // old answer in place for good.
                        .SetProperty(a => a.Latitude, (double?)null)
                        .SetProperty(a => a.Longitude, (double?)null)
                        .SetProperty(a => a.PlaceId, (int?)null)
                        .SetProperty(a => a.LocationReadUtc, (DateTime?)null)

                        // The faces belonged to the picture that was there
                        // before. Clearing the date has the pass look again,
                        // and the rows themselves go with the rendition.
                        .SetProperty(a => a.FacesDetectedUtc, (DateTime?)null)

                        // Back to pending, so the generating half remakes what
                        // was just cleared. A file that had failed to decode gets
                        // another chance too - its bytes are not the ones that
                        // failed any more.
                        .SetProperty(a => a.Status, asset.Status),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task SetCreatedDatesAsync(
        IReadOnlyList<(int AssetId, DateTime CreatedUtc)> dates,
        CancellationToken cancellationToken = default)
    {
        foreach ((int assetId, DateTime createdUtc) in dates)
        {
            await _db.Assets
                .Where(a => a.Id == assetId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(a => a.CreatedUtc, createdUtc),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task RemoveAsync(
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default)
    {
        if (assetIds.Count == 0)
        {
            return;
        }

        // Chunked: SQLite has a hard limit on parameters per statement, and a
        // library can easily lose more rows than that in one go.
        foreach (int[] chunk in assetIds.Chunk(400))
        {
            await _db.Assets
                .Where(a => chunk.Contains(a.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task<int> CountAsync(int photoSourceId, CancellationToken cancellationToken = default) =>
        _db.Assets.CountAsync(a => a.PhotoSourceId == photoSourceId, cancellationToken);

    public async Task UpdateThumbnailsAsync(
        IReadOnlyList<ThumbnailUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0)
        {
            return;
        }

        foreach (ThumbnailUpdate update in updates)
        {
            await _db.Assets
                .Where(a => a.Id == update.AssetId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(a => a.ThumbnailName, update.ThumbnailName)
                        .SetProperty(a => a.Width, update.Width)
                        .SetProperty(a => a.Height, update.Height)
                        .SetProperty(a => a.PerceptualHash, update.PerceptualHash)
                        .SetProperty(a => a.TakenUtc, update.TakenUtc)
                        .SetProperty(a => a.ContentHash, update.ContentHash)

                        // Re-derived from the file every time, so writing them
                        // again cannot disagree with it. PlaceId is deliberately
                        // absent: it is derived from these rather than from the
                        // file, and clearing it here would cost a photograph its
                        // place every time the cache was rebuilt.
                        .SetProperty(a => a.Latitude, update.Latitude)
                        .SetProperty(a => a.Longitude, update.Longitude)
                        .SetProperty(a => a.Status, AssetStatus.Ready),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task UpdateVideoKeyframesAsync(
        IReadOnlyList<VideoKeyframeUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0)
        {
            return;
        }

        foreach (VideoKeyframeUpdate update in updates)
        {
            // The clip's previous frames go first. Their files have already been
            // overwritten under the same names where the video is unchanged, and
            // where it changed the names moved - either way the old rows describe
            // frames that are no longer the ones on disk.
            await _db.VideoKeyframes
                .Where(k => k.AssetId == update.AssetId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            _db.VideoKeyframes.AddRange(update.Keyframes.Select(frame => new VideoKeyframe
            {
                AssetId = update.AssetId,
                Ordinal = frame.Ordinal,
                Position = frame.Position,
                ThumbnailName = frame.ThumbnailName,
            }));

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // The poster becomes the row's thumbnail, which is the whole of what
            // makes a video draw itself in the grid: nothing downstream has to
            // learn that this rendition came out of a container rather than off
            // the front of a JPEG.
            await _db.Assets
                .Where(a => a.Id == update.AssetId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(a => a.ThumbnailName, update.PosterName)
                        .SetProperty(a => a.Duration, update.Duration)
                        .SetProperty(a => a.Width, update.SourceWidth)
                        .SetProperty(a => a.Height, update.SourceHeight)

                        // Off Skipped, where the scan parked it. The clip now has
                        // renditions like anything else, and leaving it Skipped
                        // would have the grid keep treating it as unprepared.
                        .SetProperty(a => a.Status, AssetStatus.Ready)

                        // Cleared so the face pass looks at this clip again. Its
                        // frames are new files, and whatever was recorded against
                        // the video before describes frames that have gone.
                        .SetProperty(a => a.FacesDetectedUtc, (DateTime?)null),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task RecordLocationsAsync(
        IReadOnlyList<PhotoLocation> located, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(located);

        foreach (PhotoLocation location in located)
        {
            await _db.Assets
                .Where(a => a.Id == location.AssetId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(a => a.Latitude, location.Latitude)
                        .SetProperty(a => a.Longitude, location.Longitude)
                        .SetProperty(a => a.PlaceId, location.PlaceId)

                        // Written last in the list and always, including when the
                        // two above are null. It is the record of having asked,
                        // and without it the file is opened again on every run
                        // for the rest of the library's life.
                        .SetProperty(a => a.LocationReadUtc, location.ReadUtc),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<AssetFile>> FindSharingAsync(
        string thumbnailName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailName);

        var rows = await _db.Assets
            .AsNoTracking()
            .Where(a => a.ThumbnailName == thumbnailName)
            .Join(
                _db.Set<PhotoSource>(),
                asset => asset.PhotoSourceId,
                source => source.Id,
                (asset, source) => new { asset.Id, source.Path, asset.RelativePath })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(r =>
                new AssetFile(r.Id, Path.Combine(r.Path, r.RelativePath), r.Path)),
        ];
    }

    public async Task<PhotoFacts?> FindFactsAsync(
        int assetId, CancellationToken cancellationToken = default)
    {
        var row = await _db.Assets
            .AsNoTracking()
            .Where(a => a.Id == assetId)
            .Join(
                _db.Set<PhotoSource>(),
                asset => asset.PhotoSourceId,
                source => source.Id,
                (asset, source) => new
                {
                    asset.Id, source.Path, asset.RelativePath, asset.Length,
                    asset.Width, asset.Height, asset.TakenUtc, asset.ModifiedUtc,
                    asset.CreatedUtc, asset.ContentHash,

                    // A left join rather than a second query, and both stay null
                    // for the five photographs in six that carry no coordinates.
                    PlaceName = _db.Places
                        .Where(place => place.Id == asset.PlaceId)
                        .Select(place => place.Name)
                        .FirstOrDefault(),
                    PlaceCountry = _db.Places
                        .Where(place => place.Id == asset.PlaceId)
                        .Select(place => place.CountryCode)
                        .FirstOrDefault(),
                    PlaceRegion = _db.Places
                        .Where(place => place.Id == asset.PlaceId)
                        .Select(place => place.Admin1Code)
                        .FirstOrDefault(),
                })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : new PhotoFacts(
                row.Id,
                Path.GetFileName(row.RelativePath),
                FolderTree.FolderOf(row.RelativePath),
                Path.Combine(row.Path, row.RelativePath),
                row.Length,
                row.Width,
                row.Height,
                row.TakenUtc,
                row.ModifiedUtc,
                row.ContentHash,

                // "Tsim Sha Tsui, Hong Kong" - smallest first, as an address is
                // written. The district on its own is not much use to anyone who
                // did not already know where it is, and in this library the
                // gazetteer's answer for a dense city is always a district.
                Describe(row.PlaceName, row.PlaceRegion, row.PlaceCountry),
                row.CreatedUtc);
    }

    /// <summary>
    /// A place as a person would write it, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Smallest first, as an address is written: "Kampung Bukit Tinggi, Pahang,
    /// Malaysia". The district alone means little to anyone who does not already
    /// know where it is, which in a library of holidays is the point.
    ///
    /// <para>Each rung is dropped when it repeats one already there. City-states
    /// are the reason: the gazetteer holds Singapore as a place, as a country
    /// and sometimes as its own region, and "Singapore, Singapore, Singapore"
    /// reads as a bug rather than as an address.</para>
    /// </remarks>
    private static string? Describe(string? place, string? admin1Code, string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(place))
        {
            return null;
        }

        List<string> parts = [place.Trim()];

        foreach (string? wider in new[]
                 {
                     RegionNames.Of(countryCode, admin1Code),
                     CountryNames.Of(countryCode),
                 })
        {
            if (!string.IsNullOrWhiteSpace(wider)
                && !parts.Contains(wider, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(wider);
            }
        }

        return string.Join(", ", parts);
    }

    public async Task<AssetToRemove?> FindForRemovalAsync(
        int assetId, CancellationToken cancellationToken = default)
    {
        var row = await _db.Assets
            .AsNoTracking()
            .Where(a => a.Id == assetId)
            .Join(
                _db.Set<PhotoSource>(),
                asset => asset.PhotoSourceId,
                source => source.Id,
                (asset, source) => new { asset.Id, source.Path, asset.RelativePath, asset.ThumbnailName })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        int faces = await _db.Faces
            .CountAsync(face => face.AssetId == assetId, cancellationToken)
            .ConfigureAwait(false);

        int names = await _db.FaceAssignments
            .Where(assignment => assignment.Source == AssignmentSource.Confirmed)
            .Join(_db.Faces, a => a.FaceId, face => face.Id, (a, face) => face.AssetId)
            .CountAsync(id => id == assetId, cancellationToken)
            .ConfigureAwait(false);

        int otherCopies = row.ThumbnailName is null
            ? 0
            : await _db.Assets
                .CountAsync(
                    a => a.ThumbnailName == row.ThumbnailName && a.Id != assetId,
                    cancellationToken)
                .ConfigureAwait(false);

        return new AssetToRemove(
            row.Id,
            Path.GetFileName(row.RelativePath),
            Path.Combine(row.Path, row.RelativePath),
            row.Path,
            row.ThumbnailName,
            faces,
            names,
            otherCopies);
    }

    public async Task TurnAsync(
        IReadOnlyList<int> assetIds, int degrees, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0)
        {
            return;
        }

        // Added to whatever turn was already recorded and folded back into a
        // quarter, so four turns one way is no turn rather than a full circle
        // the preparation pass would have to work through.
        foreach (int[] chunk in assetIds.Chunk(400))
        {
            await _db.Assets
                .Where(a => chunk.Contains(a.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        a => a.Rotation, a => (a.Rotation + degrees % 360 + 360) % 360),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task SetRotationAsync(
        IReadOnlyList<int> assetIds, int rotation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0)
        {
            return;
        }

        int settled = ((rotation % 360) + 360) % 360;

        foreach (int[] chunk in assetIds.Chunk(400))
        {
            await _db.Assets
                .Where(a => chunk.Contains(a.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(a => a.Rotation, settled),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task MarkFailedAsync(
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default)
    {
        if (assetIds.Count == 0)
        {
            return;
        }

        foreach (int[] chunk in assetIds.Chunk(400))
        {
            await _db.Assets
                .Where(a => chunk.Contains(a.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(a => a.Status, AssetStatus.Failed),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task<int> CountWithThumbnailsAsync(CancellationToken cancellationToken = default) =>
        _db.Assets.CountAsync(a => a.ThumbnailName != null, cancellationToken);

    public async Task<HashSet<string>> GetReferencedThumbnailNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return referenced;
        }

        // Chunked for the same reason RemoveAsync is: SQLite caps parameters per
        // statement, and one edit can change more files than that.
        foreach (string[] chunk in names.Chunk(400))
        {
            List<string> found = await _db.Assets
                .AsNoTracking()
                .Where(a => a.ThumbnailName != null && chunk.Contains(a.ThumbnailName))
                .Select(a => a.ThumbnailName!)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            referenced.UnionWith(found);
        }

        return referenced;
    }

    public async Task<IReadOnlyList<AssetRendition>> ListRenditionsAsync(
        int photoSourceId,
        CancellationToken cancellationToken = default) =>
        await _db.Assets.AsNoTracking()
            .Where(a => a.PhotoSourceId == photoSourceId)
            .OrderBy(a => a.Id)
            .Select(a => new AssetRendition(a.Id, a.ThumbnailName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<HashSet<string>> GetThumbnailNamesExceptAsync(
        int photoSourceId,
        CancellationToken cancellationToken = default)
    {
        // One column scan. There is no index on ThumbnailName and adding one for
        // a query that runs once per detach would cost more than it saves.
        List<string> names = await _db.Assets.AsNoTracking()
            .Where(a => a.PhotoSourceId != photoSourceId && a.ThumbnailName != null)
            .Select(a => a.ThumbnailName!)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<int, int>> GetCountsBySourceAsync(
        CancellationToken cancellationToken = default)
    {
        var grouped = await _db.Assets
            .AsNoTracking()
            .GroupBy(a => a.PhotoSourceId)
            .Select(g => new { PhotoSourceId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return grouped.ToDictionary(g => g.PhotoSourceId, g => g.Count);
    }
}

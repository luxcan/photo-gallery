using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IGalleryReader"/>
public sealed class SqliteGalleryReader : IGalleryReader
{
    private readonly GalleryDbContext _db;

    public SqliteGalleryReader(GalleryDbContext db) => _db = db;

    public async Task<IReadOnlyList<PendingThumbnail>> GetThumbnailCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // Newest first - the same order the grid shows - so a pass fills the
        // screen the user is actually looking at. Working in scan order instead
        // means thousands of pictures are prepared before any of them is one of
        // the ones on display.
        //
        // Joined to sources so the absolute path is built in the query rather
        // than by looking a source up per row.
        // Failed and Skipped are excluded here rather than filtered later: a file
        // that will not decode, and a video that has no rendition to make, must
        // not cost a read on every pass for the rest of time.
        var rows = await _db.Assets
            .AsNoTracking()
            .Where(a => a.Kind == AssetKind.Photo
                     && a.Status != AssetStatus.Failed
                     && a.Status != AssetStatus.Skipped)
            .OrderByDescending(a => a.TakenUtc ?? a.ModifiedUtc)
            .ThenByDescending(a => a.Id)
            .Join(
                _db.Set<PhotoSource>(),
                asset => asset.PhotoSourceId,
                source => source.Id,
                (asset, source) => new
                {
                    asset.Id,
                    source.Path,
                    asset.RelativePath,
                    asset.ThumbnailName,
                    asset.Rotation,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(r => new PendingThumbnail(
                r.Id, Path.Combine(r.Path, r.RelativePath), r.ThumbnailName, r.Rotation))
            .ToList();
    }

    public async Task<IReadOnlyList<PendingVideo>> GetVideoCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // Newest first, as the preparing pass is, and for the same reason: a
        // pass that runs for hours should fill the part of the grid the user is
        // actually looking at first.
        //
        // Failed is excluded and Skipped deliberately is not. Skipped is exactly
        // where the scan parks a video, so it is the state nearly every
        // outstanding clip is in; Failed is a container that has already been
        // opened once and would not decode.
        //
        // Quarantined copies are left out for the reason the face pass leaves
        // them out: they are not in the library any more, and this is the most
        // expensive pass in the app to spend on files the user has dealt with.
        var rows = await _db.Assets
            .AsNoTracking()
            .Where(a => a.Kind == AssetKind.Video
                     && a.Status != AssetStatus.Failed
                     && a.QuarantinedUtc == null)
            .OrderByDescending(a => a.TakenUtc ?? a.ModifiedUtc)
            .ThenByDescending(a => a.Id)
            .Join(
                _db.Set<PhotoSource>(),
                asset => asset.PhotoSourceId,
                source => source.Id,
                (asset, source) => new
                {
                    asset.Id,
                    source.Path,
                    asset.RelativePath,
                    asset.Length,
                    asset.ModifiedUtc,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(r => new PendingVideo(
                r.Id,
                Path.Combine(r.Path, r.RelativePath),
                r.RelativePath,
                r.Length,
                r.ModifiedUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<FaceScanCandidate>> GetFaceCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // Ready with a rendition, whether that rendition was decoded from a
        // photograph or taken out of a video. Nothing here needs to know which:
        // a keyframe is written into the same store, under the same kind of
        // name, and the detector reads it the same way. That is what [08] means
        // by keyframes feeding the face pipeline unchanged.
        //
        // Newest first for the same reason the preparing pass uses that order -
        // it is what the user is looking at.
        //
        // Copies set aside as redundant are skipped for the same reason the grid
        // skips them: they are not in the library any more, and spending twenty
        // minutes finding faces in pictures the user has already dealt with only
        // fills the people screens with questions about files that have moved.
        //
        // Already-scanned pictures come back too, carrying when they were
        // scanned. Whether they need looking at again is a question about the
        // file on disk, and only the pass can see that - the same rule the
        // preparing pass follows when it trusts the disk over the row's claim.
        // It is also what puts a video back in the queue when its frames are
        // remade: the keyframe pass clears the marker, and a newer rendition
        // than the marker is the other half of the same test.
        //
        // One rendition per row, which is a video's poster and today is a
        // video's only frame - the shell hands back the one picture it has
        // decided represents the file. The moment an extractor that seeks lands,
        // this is where the other frames have to be picked up: their faces
        // cannot simply be added to the poster's, because IFaceRepository.Save
        // replaces an asset's faces rather than adding to them, and because a
        // box found nine minutes in does not sit anywhere on the poster.
        List<FaceScanCandidate> candidates = await _db.Assets
            .AsNoTracking()
            .Where(a => a.Status == AssetStatus.Ready
                     && a.QuarantinedUtc == null
                     && a.ThumbnailName != null)
            .OrderByDescending(a => a.TakenUtc ?? a.ModifiedUtc)
            .ThenByDescending(a => a.Id)
            .Select(a => new FaceScanCandidate(a.Id, a.ThumbnailName!, a.FacesDetectedUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates;
    }

    public async Task<IReadOnlyList<LocationCandidate>> GetLocationCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // Ready and in the library, and never yet asked where it was taken.
        //
        // Joined to sources because most of these will need their original
        // opened, and the pass has to know which share that is before it touches
        // anything - a photograph on an absent NAS must be left alone rather than
        // recorded as having no coordinates.
        var rows = await _db.Assets
            .AsNoTracking()
            .Where(a => a.Kind == AssetKind.Photo
                     && a.Status == AssetStatus.Ready
                     && a.QuarantinedUtc == null
                     && a.LocationReadUtc == null)
            .OrderByDescending(a => a.TakenUtc ?? a.ModifiedUtc)
            .ThenByDescending(a => a.Id)
            .Join(
                _db.Set<PhotoSource>(),
                asset => asset.PhotoSourceId,
                source => source.Id,
                (asset, source) => new
                {
                    asset.Id,
                    source.Path,
                    asset.RelativePath,
                    asset.Latitude,
                    asset.Longitude,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(r => new LocationCandidate(
                r.Id, Path.Combine(r.Path, r.RelativePath), r.Path, r.Latitude, r.Longitude))
            .ToList();
    }

    public async Task<IReadOnlyList<ContentScanCandidate>> GetContentCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // Ready, in the library, with a rendition to look at, and with nothing
        // already recorded about what it is of. That last clause is the whole
        // resumability marker, so it is a left join rather than a date.
        //
        // Newest first for the same reason every other pass uses that order: it
        // is what the user is looking at while the hour passes.
        return await _db.Assets
            .AsNoTracking()
            .Where(a => a.Kind == AssetKind.Photo
                     && a.Status == AssetStatus.Ready
                     && a.QuarantinedUtc == null
                     && a.ThumbnailName != null
                     && !_db.PhotoContent.Any(content => content.AssetId == a.Id))
            .OrderByDescending(a => a.TakenUtc ?? a.ModifiedUtc)
            .ThenByDescending(a => a.Id)
            .Select(a => new ContentScanCandidate(a.Id, a.ThumbnailName!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GalleryPage> QueryAsync(
        GalleryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Asset> rows = Filter(query);

        // A description's answer is its order, so it is not sorted again. The
        // list is already capped by the search, small enough to arrange in
        // memory, and putting it in date order here would discard the only thing
        // that made these the pictures offered.
        if (query.RankedAssetIds is { Count: > 0 } ranked)
        {
            return await RankedPageAsync(rows, ranked, cancellationToken).ConfigureAwait(false);
        }

        int total = await rows.CountAsync(cancellationToken).ConfigureAwait(false);

        // By capture date, falling back to the file's own date. The tie-break is
        // not cosmetic: 1,964 photos in this library share an exact timestamp
        // with another, so without it the order inside those groups is undefined
        // and the one-photo view could revisit a picture while walking forward.
        // It reverses with the order for the same reason - a tie-break that kept
        // its direction would make those groups run backwards against the rest.
        IQueryable<Asset> ordered = query.SortOrder == GallerySortOrder.OldestFirst
            ? rows.OrderBy(SortDate).ThenBy(a => a.Id)
            : rows.OrderByDescending(SortDate).ThenByDescending(a => a.Id);

        if (query.Skip > 0)
        {
            ordered = ordered.Skip(query.Skip);
        }

        if (query.Take > 0)
        {
            ordered = ordered.Take(query.Take);
        }

        List<Asset> page = await ordered
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The roots, so each row can name the file where it actually lives. A
        // handful of rows read once beats a join repeated down the page.
        Dictionary<int, string> roots = await _db.Set<PhotoSource>()
            .AsNoTracking()
            .ToDictionaryAsync(source => source.Id, source => source.Path, cancellationToken)
            .ConfigureAwait(false);

        return new GalleryPage([.. page.Select(asset => ToItem(asset, roots))], total);
    }

    public async Task<IReadOnlyList<FolderNode>> GetFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        // Two columns for every picture - 11,482 rows and about 430 KB - rather
        // than a GROUP BY on a path expression. SQLite has no path functions,
        // EF cannot translate the string arithmetic that imitates them, and the
        // result would give leaf counts only while the tree needs ancestors
        // rolled up. Measured at 12 ms end to end, done once per view.
        // Photos only, so a folder's count means the same thing as the number of
        // tiles selecting it puts in the grid.
        var rows = await _db.Assets
            .AsNoTracking()
            .Where(a => a.Kind == AssetKind.Photo && a.QuarantinedUtc == null)
            .Select(a => new { a.PhotoSourceId, a.RelativePath })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, string> sourceNames = await _db.Set<PhotoSource>()
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Path, cancellationToken)
            .ConfigureAwait(false);

        return FolderTree.Build(
            rows.Select(r => (r.PhotoSourceId, r.RelativePath)), sourceNames);
    }

    /// <summary>
    /// The ranked photographs, in the order they were ranked.
    /// </summary>
    /// <remarks>
    /// Rows that no longer pass the other filters simply do not appear: a
    /// description ranked over the whole library can name a picture in a folder
    /// the user has since narrowed away, and the honest answer is to leave it out
    /// rather than to widen what was asked for.
    /// </remarks>
    private async Task<GalleryPage> RankedPageAsync(
        IQueryable<Asset> rows, IReadOnlyList<int> ranked, CancellationToken cancellationToken)
    {
        List<Asset> matched = await rows
            .Where(asset => ranked.Contains(asset.Id))
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, int> placeOf = new(ranked.Count);
        for (int place = 0; place < ranked.Count; place++)
        {
            placeOf[ranked[place]] = place;
        }

        Dictionary<int, string> roots = await _db.Set<PhotoSource>()
            .AsNoTracking()
            .ToDictionaryAsync(source => source.Id, source => source.Path, cancellationToken)
            .ConfigureAwait(false);

        List<GalleryItem> items =
        [
            .. matched
                .OrderBy(asset => placeOf[asset.Id])
                .Select(asset => ToItem(asset, roots)),
        ];

        return new GalleryPage(items, items.Count);
    }

    private IQueryable<Asset> Filter(GalleryQuery query)
    {
        // Copies set aside as redundant are not in the library any more. Their
        // rows survive so they can be put back, but a picture whose file has
        // been moved out has no business in the grid.
        IQueryable<Asset> rows = _db.Assets.Where(a => a.QuarantinedUtc == null);

        if (query.IncludeVideos)
        {
            // A video with no poster yet is left out rather than drawn as a grey
            // cell. See GalleryQuery.IncludeVideos: a photograph's placeholder is
            // filled in minutes later by the pass that follows the scan, and a
            // video's waits on one somebody has to choose to start.
            rows = rows.Where(a => a.Kind == AssetKind.Photo || a.ThumbnailName != null);
        }
        else
        {
            rows = rows.Where(a => a.Kind == AssetKind.Photo);
        }

        if (query.PhotoSourceId is int sourceId)
        {
            rows = rows.Where(a => a.PhotoSourceId == sourceId);
        }

        if (query.PersonId is int personId)
        {
            // The point of the whole feature: every picture of one person, from
            // every folder, including the ones whose names mention nobody.
            IQueryable<int> theirs =
                from face in _db.Faces
                join assignment in _db.FaceAssignments on face.Id equals assignment.FaceId
                where assignment.PersonId == personId
                   && assignment.Source == AssignmentSource.Confirmed
                select face.AssetId;

            rows = rows.Where(a => theirs.Contains(a.Id));
        }

        if (query.Place is PlaceFilter place)
        {
            rows = PlaceRestriction.Apply(rows, place, _db);
        }

        if (!string.IsNullOrWhiteSpace(query.FolderPath))
        {
            (string from, string before) = FolderTree.SubtreeBounds(query.FolderPath);
            rows = rows.Where(a =>
                string.Compare(a.RelativePath, from) >= 0
                && string.Compare(a.RelativePath, before) < 0);
        }

        return rows;
    }

    /// <summary>
    /// The order the grid is in, expressed so the database can sort by it.
    /// </summary>
    /// <remarks>
    /// The same rule as <see cref="AssetDates.BestGuess"/> and it has to stay
    /// that way: this decides where a picture sits and that decides what the
    /// picture says it is. A method group cannot be translated to SQL, so the
    /// rule is written twice - and <c>Query_OrdersByTheSameDateItReports</c>
    /// fails the moment the two disagree.
    /// </remarks>
    private static readonly Expression<Func<Asset, DateTime>> SortDate =
        asset => asset.TakenUtc
                 ?? (asset.CreatedUtc != default && asset.CreatedUtc < asset.ModifiedUtc
                     ? asset.CreatedUtc
                     : asset.ModifiedUtc);

    private static GalleryItem ToItem(Asset asset, Dictionary<int, string> roots) =>
        new(asset.Id,
            asset.RelativePath,
            Path.GetFileName(asset.RelativePath),
            FolderTree.FolderOf(asset.RelativePath),
            roots.TryGetValue(asset.PhotoSourceId, out string? root)
                ? Path.Combine(root, asset.RelativePath)
                : string.Empty,
            asset.ThumbnailName,
            asset.TakenUtc,
            AssetDates.BestGuess(asset.TakenUtc, asset.CreatedUtc, asset.ModifiedUtc),
            asset.Rotation,
            asset.Kind,
            asset.Duration);
}

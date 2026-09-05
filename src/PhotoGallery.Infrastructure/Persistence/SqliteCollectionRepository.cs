using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="ICollectionRepository"/>
public sealed class SqliteCollectionRepository : ICollectionRepository
{
    /// <summary>How many covers the band's mosaic can show for one shelf.</summary>
    private const int MosaicTiles = 4;

    private readonly GalleryDbContext _db;

    public SqliteCollectionRepository(GalleryDbContext db) => _db = db;

    /// <inheritdoc/>
    /// <remarks>
    /// Two queries and the arithmetic in memory, rather than three correlated
    /// subqueries down each column of one. A library has a handful of shelves
    /// and a few hundred albums - the whole of the second query is smaller than
    /// the wall this screen already draws - and "the cover of the most recently
    /// taken album on it" written as SQL is an ordered join inside a projection,
    /// which is the shape that quietly stops being ordered.
    /// </remarks>
    public async Task<IReadOnlyList<CollectionSummary>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var shelves = await _db.Collections
            .AsNoTracking()
            .OrderBy(collection => collection.Name)
            .Select(collection => new { collection.Id, collection.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var albums = await _db.Albums
            .AsNoTracking()
            .Where(album => album.CollectionId != null)
            .Select(album => new
            {
                CollectionId = album.CollectionId!.Value,
                album.EndUtc,
                Photos = album.Members.Count,
                Cover = _db.Assets
                    .Where(asset => asset.Id == album.CoverAssetId)
                    .Select(asset => asset.ThumbnailName)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. shelves.Select(shelf =>
            {
                var on = albums.Where(album => album.CollectionId == shelf.Id).ToList();

                return new CollectionSummary(
                    shelf.Id,
                    shelf.Name,
                    on.Count,
                    on.Sum(album => album.Photos),
                    [
                        .. on.Where(album => album.Cover != null)
                            .OrderByDescending(album => album.EndUtc)
                            .Select(album => album.Cover!)
                            .Take(MosaicTiles)
                    ]);
            })
        ];
    }

    public async Task<int> CreateAsync(
        string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var collection = new Collection
        {
            Name = name.Trim(),
            CreatedUtc = DateTime.UtcNow,
            NamedUtc = DateTime.UtcNow,
        };

        _db.Collections.Add(collection);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return collection.Id;
    }

    public async Task RenameAsync(
        int collectionId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Collection? collection = await _db.Collections
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return;
        }

        collection.Name = name.Trim();
        collection.NamedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        int collectionId, CancellationToken cancellationToken = default)
    {
        Collection? collection = await _db.Collections
            .FirstOrDefaultAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return;
        }

        // Ignoring the filter on purpose. An album that has been removed still
        // holds the shelf it was on, and it is out of every query in the app
        // until somebody restores it - at which point it would come back
        // pointing at a collection that is gone.
        await _db.Albums
            .IgnoreQueryFilters()
            .Where(album => album.CollectionId == collectionId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(album => album.CollectionId, (int?)null),
                cancellationToken)
            .ConfigureAwait(false);

        collection.DeletedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CollectionFillResult> SetAlbumsAsync(
        int collectionId,
        IReadOnlyList<int> albumIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(albumIds);

        bool exists = await _db.Collections
            .AnyAsync(row => row.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return CollectionFillResult.Nothing;
        }

        HashSet<int> wanted = [.. albumIds];

        List<Album> touched = await _db.Albums
            .Where(album => album.CollectionId == collectionId || wanted.Contains(album.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int added = 0;
        int removed = 0;
        int kept = 0;
        HashSet<int> left = [];

        foreach (Album album in touched)
        {
            if (!wanted.Contains(album.Id))
            {
                album.CollectionId = null;
                removed++;
                continue;
            }

            if (album.CollectionId != collectionId)
            {
                // Where it came from, before the column is overwritten. An album
                // is on one shelf, so joining this one is leaving that one, and
                // the screen has to be able to name it.
                if (album.CollectionId is int was)
                {
                    left.Add(was);
                }

                album.CollectionId = collectionId;
                added++;
            }

            // Putting a suggestion on a shelf is keeping it. One write rather
            // than a keep and then a move, so the two cannot come apart.
            if (album.Origin == AlbumOrigin.Proposed)
            {
                album.Origin = AlbumOrigin.Accepted;
                kept++;
            }
        }

        List<string> from = left.Count == 0
            ? []
            : await _db.Collections
                .AsNoTracking()
                .Where(collection => left.Contains(collection.Id))
                .OrderBy(collection => collection.Name)
                .Select(collection => collection.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CollectionFillResult(added, removed, kept, from);
    }

    public async Task<string?> SetAlbumCollectionAsync(
        int albumId,
        int? collectionId,
        CancellationToken cancellationToken = default)
    {
        Album? album = await _db.Albums
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null || album.CollectionId == collectionId)
        {
            return null;
        }

        if (collectionId is int wanted
            && !await _db.Collections
                .AnyAsync(row => row.Id == wanted, cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        // Read before the column is overwritten. A shelf this album has never
        // heard of is not a shelf it left - the screen reads a dangling id as no
        // collection, so saying it came off one would be a sentence about a row
        // that is not there.
        string? left = album.CollectionId is int was
            ? await _db.Collections
                .AsNoTracking()
                .Where(collection => collection.Id == was)
                .Select(collection => collection.Name)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        album.CollectionId = collectionId;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return left;
    }
}

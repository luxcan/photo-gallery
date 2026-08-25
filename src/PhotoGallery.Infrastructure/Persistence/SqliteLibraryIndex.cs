using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="ILibraryIndex"/>
public sealed class SqliteLibraryIndex : ILibraryIndex
{
    private readonly GalleryDbContext _db;

    public SqliteLibraryIndex(GalleryDbContext db) => _db = db;

    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        _db.Database.MigrateAsync(cancellationToken);

    public async Task<LibrarySettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        LibrarySettings? settings = await _db.LibrarySettings
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (settings is not null)
        {
            return settings;
        }

        settings = new LibrarySettings { Id = 1 };
        _db.LibrarySettings.Add(settings);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return settings;
    }

    public async Task SaveSettingsAsync(
        LibrarySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (_db.Entry(settings).State == EntityState.Detached)
        {
            _db.LibrarySettings.Update(settings);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PhotoSource>> GetSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.PhotoSources
            .OrderBy(s => s.AddedUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PhotoSource> AddSourceAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var source = new PhotoSource { Path = path, AddedUtc = DateTime.UtcNow };
        _db.PhotoSources.Add(source);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return source;
    }

    public async Task UpdateSourceAsync(
        PhotoSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await _db.PhotoSources
            .Where(s => s.Id == source.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.LastScanUtc, source.LastScanUtc),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RemoveSourceAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        PhotoSource? source = await _db.PhotoSources
            .FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
            .ConfigureAwait(false);

        if (source is null)
        {
            return; // already gone - removing twice is not an error
        }

        _db.PhotoSources.Remove(source);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LibraryCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        int photos = await _db.Assets
            .CountAsync(a => a.Kind == AssetKind.Photo, cancellationToken).ConfigureAwait(false);
        int videos = await _db.Assets
            .CountAsync(a => a.Kind == AssetKind.Video, cancellationToken).ConfigureAwait(false);
        int videosPrepared = await _db.Assets
            .CountAsync(
                a => a.Kind == AssetKind.Video && a.ThumbnailName != null,
                cancellationToken)
            .ConfigureAwait(false);

        // The same rows the keyframe pass leaves out of its candidates, so that
        // what is left over really is what a rescan would pick up.
        int videosUnreadable = await _db.Assets
            .CountAsync(
                a => a.Kind == AssetKind.Video
                     && a.ThumbnailName == null
                     && (a.Status == AssetStatus.Failed || a.QuarantinedUtc != null),
                cancellationToken)
            .ConfigureAwait(false);
        int thumbnails = await _db.Assets
            .CountAsync(a => a.ThumbnailName != null, cancellationToken).ConfigureAwait(false);
        int faces = await _db.Faces.CountAsync(cancellationToken).ConfigureAwait(false);
        int people = await _db.People.CountAsync(cancellationToken).ConfigureAwait(false);
        int duplicateSets = await _db.DuplicateSets
            .CountAsync(s => !s.IsResolved, cancellationToken).ConfigureAwait(false);

        // What the two optional passes still have to look at, on the same terms
        // their own candidate queries use. Counted here so the screens can say
        // "installed, and nothing has been looked at yet" as a fact rather than
        // inferring it from an empty result - a library of landscapes really can
        // have been scanned and have no faces in it, and a notice that reads
        // "not looked at yet" for ever on such a library would be a lie.
        int awaitingFaces = await _db.Assets
            .CountAsync(
                a => a.Status == AssetStatus.Ready
                     && a.QuarantinedUtc == null
                     && a.ThumbnailName != null
                     && a.FacesDetectedUtc == null,
                cancellationToken)
            .ConfigureAwait(false);

        int awaitingDescription = await _db.Assets
            .CountAsync(
                a => a.Kind == AssetKind.Photo
                     && a.Status == AssetStatus.Ready
                     && a.QuarantinedUtc == null
                     && a.ThumbnailName != null
                     && !_db.PhotoContent.Any(content => content.AssetId == a.Id),
                cancellationToken)
            .ConfigureAwait(false);

        int collections = await _db.Collections
            .CountAsync(cancellationToken).ConfigureAwait(false);

        return new LibraryCounts(
            photos, videos, videosPrepared, videosUnreadable,
            thumbnails, faces, people, duplicateSets,
            awaitingFaces, awaitingDescription, collections);
    }
}

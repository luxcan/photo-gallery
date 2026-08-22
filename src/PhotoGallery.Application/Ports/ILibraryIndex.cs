using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// The library's index. Infrastructure backs this with SQLite; the use cases
/// never learn that.
/// </summary>
public interface ILibraryIndex
{
    /// <summary>Creates the index or brings an existing one up to the current schema.</summary>
    Task MigrateAsync(CancellationToken cancellationToken = default);

    Task<LibrarySettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(LibrarySettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhotoSource>> GetSourcesAsync(CancellationToken cancellationToken = default);

    Task<PhotoSource> AddSourceAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to a source, such as when it was last scanned.</summary>
    Task UpdateSourceAsync(PhotoSource source, CancellationToken cancellationToken = default);

    /// <summary>Detaches a source. Its assets go with it; the files themselves are untouched.</summary>
    Task RemoveSourceAsync(int sourceId, CancellationToken cancellationToken = default);

    Task<LibraryCounts> GetCountsAsync(CancellationToken cancellationToken = default);
}

using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Albums;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.OpenLibrary;

/// <summary>
/// Opens a working folder: lays out its subfolders, brings the index up to the
/// current schema, and reports what is inside.
/// </summary>
public sealed class OpenLibraryHandler
{
    private readonly IWorkingFolder _workingFolder;
    private readonly ILibraryIndex _index;
    private readonly IAssetRepository _assets;
    private readonly IAppConfigStore _config;
    private readonly MoveAlbumFilesHandler? _albumMoves;

    public OpenLibraryHandler(
        IWorkingFolder workingFolder,
        ILibraryIndex index,
        IAssetRepository assets,
        IAppConfigStore config,
        MoveAlbumFilesHandler? albumMoves = null)
    {
        _workingFolder = workingFolder;
        _index = index;
        _assets = assets;
        _config = config;
        _albumMoves = albumMoves;
    }

    public async Task<OpenLibraryResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        bool wasCreated = !File.Exists(_workingFolder.DatabasePath);

        _workingFolder.EnsureCreated();
        await _index.MigrateAsync(cancellationToken).ConfigureAwait(false);

        // A file move and a SQLite update cannot be one transaction. The durable
        // journal written before every move lets opening settle whichever half
        // an interrupted process reached before the rest of the app reads paths.
        if (_albumMoves is not null)
        {
            await _albumMoves.RecoverAsync(cancellationToken).ConfigureAwait(false);
        }

        LibrarySettings settings = await _index.GetSettingsAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<PhotoSource> sources = await _index.GetSourcesAsync(cancellationToken)
            .ConfigureAwait(false);
        LibraryCounts counts = await _index.GetCountsAsync(cancellationToken)
            .ConfigureAwait(false);

        // Without this the table would show every source as empty until it was
        // scanned again, even though the files are already indexed.
        Dictionary<int, int> fileCounts = await _assets.GetCountsBySourceAsync(cancellationToken)
            .ConfigureAwait(false);

        // Recorded only once the library really opened, so a folder that failed
        // to migrate is not offered again as if it had worked. The palette is
        // not recorded here: it belongs to the library and travels with it.
        _config.RememberFolder(_workingFolder.Root);

        return new OpenLibraryResult(
            _workingFolder.Root, sources, fileCounts, counts,
            settings.Theme, settings.GalleryCellSize, settings.GallerySortOrder,
            settings.NavigationCollapsed, wasCreated);
    }
}

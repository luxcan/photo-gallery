using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.OpenLibrary;
using PhotoGallery.Application.UseCases.Preferences;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// The zoom is stored with the library, like the palette. What matters here is
/// less the round trip than the default: the column carries one of its own, and
/// if it were ever left at zero the grid would open with cells of no size.
/// </summary>
public sealed class SaveGalleryCellSizeHandlerTests : IDisposable
{
    private const double DefaultCellSize = 200d;

    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;

    public SaveGalleryCellSizeHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-zoom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();
        _index = new SqliteLibraryIndex(_db);
    }

    [Fact]
    public async Task NewLibrary_StartsAtTheDefaultSize()
    {
        LibrarySettings settings = await _index.GetSettingsAsync();

        Assert.Equal(DefaultCellSize, settings.GalleryCellSize);
    }

    [Fact]
    public async Task SavedZoom_SurvivesAReopen()
    {
        await new SaveGalleryCellSizeHandler(_index).HandleAsync(320d);

        // A fresh context proves the value reached the file, not just the
        // change tracker.
        using var reopened = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options);
        var index = new SqliteLibraryIndex(reopened);

        Assert.Equal(320d, (await index.GetSettingsAsync()).GalleryCellSize);
    }

    [Fact]
    public async Task OpenLibrary_ReportsTheSavedZoom()
    {
        await new SaveGalleryCellSizeHandler(_index).HandleAsync(280d);

        var handler = new OpenLibraryHandler(
            new WorkingFolder(_tempRoot),
            _index,
            new SqliteAssetRepository(_db),
            new JsonAppConfigStore(Path.Combine(_tempRoot, "config.json")));

        OpenLibraryResult result = await handler.HandleAsync();

        Assert.Equal(280d, result.GalleryCellSize);
    }

    [Fact]
    public async Task SavingTheSameZoomTwiceIsHarmless()
    {
        // A wheel gesture arrives one notch at a time and opening a library
        // applies the stored value through the same path, so a repeat is normal.
        var handler = new SaveGalleryCellSizeHandler(_index);

        await handler.HandleAsync(240d);
        await handler.HandleAsync(240d);

        Assert.Equal(240d, (await _index.GetSettingsAsync()).GalleryCellSize);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

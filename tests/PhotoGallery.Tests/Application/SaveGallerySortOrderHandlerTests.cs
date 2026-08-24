using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.OpenLibrary;
using PhotoGallery.Application.UseCases.Preferences;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// The grid's order is stored with the library, like the palette and the zoom.
/// </summary>
public sealed class SaveGallerySortOrderHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;

    public SaveGallerySortOrderHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-sort-{Guid.NewGuid():N}");
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
    public async Task NewLibrary_StartsNewestFirst()
    {
        // Also what a library written before the column existed reads back as,
        // because NewestFirst is the enum's zero.
        LibrarySettings settings = await _index.GetSettingsAsync();

        Assert.Equal(GallerySortOrder.NewestFirst, settings.GallerySortOrder);
    }

    [Fact]
    public async Task SavedOrder_SurvivesAReopen()
    {
        await new SaveGallerySortOrderHandler(_index).HandleAsync(GallerySortOrder.OldestFirst);

        // A fresh context proves the value reached the file, not just the
        // change tracker.
        using var reopened = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options);
        var index = new SqliteLibraryIndex(reopened);

        Assert.Equal(
            GallerySortOrder.OldestFirst,
            (await index.GetSettingsAsync()).GallerySortOrder);
    }

    [Fact]
    public async Task OpenLibrary_ReportsTheSavedOrder()
    {
        await new SaveGallerySortOrderHandler(_index).HandleAsync(GallerySortOrder.OldestFirst);

        var handler = new OpenLibraryHandler(
            new WorkingFolder(_tempRoot),
            _index,
            new SqliteAssetRepository(_db),
            new JsonAppConfigStore(Path.Combine(_tempRoot, "config.json")));

        OpenLibraryResult result = await handler.HandleAsync();

        Assert.Equal(GallerySortOrder.OldestFirst, result.GallerySortOrder);
    }

    [Fact]
    public async Task SavingTheSameOrderTwiceIsHarmless()
    {
        // The control binds straight to the gallery, so applying a stored order
        // while opening comes back through the save path as well.
        var handler = new SaveGallerySortOrderHandler(_index);

        await handler.HandleAsync(GallerySortOrder.OldestFirst);
        await handler.HandleAsync(GallerySortOrder.OldestFirst);

        Assert.Equal(
            GallerySortOrder.OldestFirst,
            (await _index.GetSettingsAsync()).GallerySortOrder);
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

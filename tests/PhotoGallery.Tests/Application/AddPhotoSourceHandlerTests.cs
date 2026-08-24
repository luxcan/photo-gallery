using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

public sealed class AddPhotoSourceHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _workingFolderRoot;
    private readonly string _photosRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;
    private readonly WorkingFolder _workingFolder;

    public AddPhotoSourceHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-test-{Guid.NewGuid():N}");
        _workingFolderRoot = Path.Combine(_tempRoot, "library");
        _photosRoot = Path.Combine(_tempRoot, "photos");
        Directory.CreateDirectory(_workingFolderRoot);
        Directory.CreateDirectory(_photosRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_workingFolderRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();
        _index = new SqliteLibraryIndex(_db);
        _workingFolder = new WorkingFolder(_workingFolderRoot);
        _workingFolder.EnsureCreated();
    }

    private AddPhotoSourceHandler NewHandler() => new(_index, _workingFolder);

    [Fact]
    public async Task Add_PersistsTheSourceAcrossReload()
    {
        await NewHandler().HandleAsync(_photosRoot);

        IReadOnlyList<PhotoSource> reloaded = await _index.GetSourcesAsync();
        Assert.Single(reloaded);
        Assert.Equal(_photosRoot, reloaded[0].Path);
    }

    [Fact]
    public async Task Add_SupportsMultipleSources()
    {
        string second = Path.Combine(_tempRoot, "camera-dump");
        Directory.CreateDirectory(second);

        await NewHandler().HandleAsync(_photosRoot);
        await NewHandler().HandleAsync(second);

        Assert.Equal(2, (await _index.GetSourcesAsync()).Count);
    }

    [Fact]
    public async Task Add_TrimsTrailingSeparators()
    {
        PhotoSource source = await NewHandler().HandleAsync(_photosRoot + @"\");

        Assert.Equal(_photosRoot, source.Path);
    }

    [Fact]
    public async Task Add_SamePathTwiceThrows()
    {
        await NewHandler().HandleAsync(_photosRoot);

        // Case-insensitive: Windows paths that differ only by case are one folder.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewHandler().HandleAsync(_photosRoot.ToUpperInvariant()));
    }

    [Fact]
    public async Task Add_TheWorkingFolderItselfIsAllowed()
    {
        // Set-up lets the user point at a folder that already holds pictures,
        // so the working folder root must be usable as a source.
        PhotoSource source = await NewHandler().HandleAsync(_workingFolderRoot);

        Assert.Equal(_workingFolderRoot, source.Path);
    }

    [Fact]
    public async Task Add_TheAppsOwnFoldersThrow()
    {
        _workingFolder.EnsureCreated();

        // Indexing these would mean cataloguing the app's own thumbnails.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewHandler().HandleAsync(_workingFolder.ThumbnailsPath));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewHandler().HandleAsync(_workingFolder.QuarantinePath));
    }

    [Fact]
    public async Task Add_AFolderNestedInAnExistingSourceThrows()
    {
        string nested = Path.Combine(_photosRoot, "2016");
        Directory.CreateDirectory(nested);
        await NewHandler().HandleAsync(_photosRoot);

        // Would otherwise index the same files twice.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewHandler().HandleAsync(nested));
    }

    [Fact]
    public async Task Add_UnreachableFolderThrows()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => NewHandler().HandleAsync(Path.Combine(_tempRoot, "does-not-exist")));
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
            // A straggling handle on the temp db is not a test failure.
        }
    }
}

using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Where the shared folder may and may not be.
/// </summary>
/// <remarks>
/// Sharing writes ordinary <c>.jpg</c> files into a folder tree. Put that tree
/// inside a photo source and the next scan indexes them as photographs, and the
/// library grows a second copy of itself every time anybody presses Refresh.
///
/// <para><strong>Both directions, which is the half that gets left out.</strong>
/// Refusing a shared folder inside a source while still allowing a source to be
/// added one level above the shared folder permits exactly the outcome the first
/// rule exists to prevent - and the second is the easier mistake to make,
/// because the code that adds a source has no reason to know sharing exists.</para>
/// </remarks>
public sealed class SharedFolderRulesTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;
    private readonly WorkingFolder _working;

    public SharedFolderRulesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-folders-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _working = new WorkingFolder(Path.Combine(_root, "library"));
        _working.EnsureCreated();

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={_working.DatabasePath}")
                .Options);
        _db.Database.Migrate();

        _index = new SqliteLibraryIndex(_db);
    }

    [Fact]
    public async Task AFolderOutsideThePhotographsIsFine()
    {
        await AddSourceAsync(Folder("photos"));

        await Sharing().HandleAsync(Folder("shared"));

        LibrarySettings settings = await _index.GetSettingsAsync();
        Assert.Equal(Folder("shared"), settings.SharedFolder);
    }

    [Fact]
    public async Task AFolderInsideThePhotographsIsRefusedWithAReason()
    {
        await AddSourceAsync(Folder("photos"));

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sharing().HandleAsync(Folder(@"photos\shared")));

        Assert.Contains("overlaps the photos", refused.Message);
    }

    [Fact]
    public async Task AFolderHoldingThePhotographsIsRefusedToo()
    {
        // The same rule from the other side: a shared folder one level above a
        // source still ends up with its files inside that source's tree.
        await AddSourceAsync(Folder(@"outer\photos"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sharing().HandleAsync(Folder("outer")));
    }

    [Fact]
    public async Task APhotoSourceHoldingTheSharedFolderIsRefused()
    {
        // The half that is easy to leave out, and the one that reopens the hole:
        // nominate the folder correctly, then add a source one level above it.
        await Sharing().HandleAsync(Folder(@"outer\shared"));

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AddSourceAsync(Folder("outer")));

        Assert.Contains("shares answers through", refused.Message);
    }

    [Fact]
    public async Task APhotoSourceInsideTheSharedFolderIsRefused()
    {
        await Sharing().HandleAsync(Folder("shared"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AddSourceAsync(Folder(@"shared\thumbs")));
    }

    [Fact]
    public async Task TheAppsOwnFoldersAreRefused()
    {
        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sharing().HandleAsync(_working.ThumbnailsPath));

        Assert.Contains("belongs to Photo Gallery", refused.Message);
    }

    [Fact]
    public async Task AFolderThatIsNotThereIsRefusedRatherThanRemembered()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => Sharing().HandleAsync(Path.Combine(_root, "nowhere")));
    }

    [Fact]
    public async Task StoppingSharingForgetsTheFolderAndNothingElse()
    {
        await Sharing().HandleAsync(Folder("shared"));
        await Sharing().ClearAsync();

        LibrarySettings settings = await _index.GetSettingsAsync();

        Assert.Null(settings.SharedFolder);
        Assert.NotEqual(Guid.Empty, settings.MachineId);
    }

    private SetSharedFolderHandler Sharing() => new(_index, _working);

    private Task AddSourceAsync(string path) =>
        new AddPhotoSourceHandler(_index, _working).HandleAsync(path);

    private string Folder(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        _db.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A handle the runtime has not finished with; the folder is under
            // the temp root.
        }
    }
}

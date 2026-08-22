using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// That a crawl says which folder it is in.
/// </summary>
/// <remarks>
/// A crawl cannot know how many files it will find until it has found them, so
/// its bar can only report that something is happening. The folder name is the
/// one honest sign of movement it has, and without it a long walk over a network
/// share is indistinguishable from one that has hung.
/// </remarks>
public sealed class ScanFolderProgressTests : IDisposable
{
    private readonly string _root;
    private readonly string _photos;
    private readonly GalleryDbContext _db;
    private readonly WorkingFolder _workingFolder;

    public ScanFolderProgressTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-scanfolder-{Guid.NewGuid():N}");
        _photos = Path.Combine(_root, "photos");
        Directory.CreateDirectory(_photos);

        _workingFolder = new WorkingFolder(Path.Combine(_root, "working"));
        _workingFolder.EnsureCreated();

        _db = new GalleryDbContext(new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
            .Options);
        _db.Database.Migrate();
    }

    [Fact]
    public async Task Scan_NamesEachFolderAsItReachesIt()
    {
        // Deliberately small folders. Reporting only every 250 files would never
        // name any of these, and a real library is mostly folders this size.
        WritePhoto(@"20230201\a.jpg");
        WritePhoto(@"20230203 - Chingay\b.jpg");
        WritePhoto(@"20230405 - Beach\c.jpg");

        var seen = new List<string>();
        var progress = new Progress<ScanProgress>(p => seen.Add(p.Folder));

        PhotoSource source = await AddSourceAsync();
        await NewHandler().HandleAsync(source.Id, progress);

        // Progress<T> posts, so give the callbacks a moment to arrive.
        await Task.Delay(150);

        Assert.Contains(@"20230201", seen);
        Assert.Contains(@"20230203 - Chingay", seen);
        Assert.Contains(@"20230405 - Beach", seen);
    }

    [Fact]
    public async Task Scan_ReportsNoFolderForFilesAtTheRoot()
    {
        // Empty rather than a guess: the caller falls back to naming the source
        // itself, which is the truthful thing to show for a file with no folder.
        WritePhoto("loose.jpg");

        var seen = new List<string>();
        var progress = new Progress<ScanProgress>(p => seen.Add(p.Folder));

        PhotoSource source = await AddSourceAsync();
        await NewHandler().HandleAsync(source.Id, progress);
        await Task.Delay(150);

        Assert.All(seen, folder => Assert.Equal(string.Empty, folder));
    }

    private ScanPhotoSourceHandler NewHandler() =>
        new(new SqliteLibraryIndex(_db),
            new SqliteAssetRepository(_db),
            new MediaFileWalker(_workingFolder),
            new FileSystemThumbnailStore(_workingFolder));

    private Task<PhotoSource> AddSourceAsync() =>
        new AddPhotoSourceHandler(new SqliteLibraryIndex(_db), _workingFolder)
            .HandleAsync(_photos);

    private void WritePhoto(string relativePath)
    {
        string full = Path.Combine(_photos, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, $"bytes of {relativePath}");
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
            // A temp folder that will not go is not a failed test.
        }
    }
}

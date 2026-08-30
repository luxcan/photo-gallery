using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Sharing;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// How long the deleting overlay stays up.
/// </summary>
/// <remarks>
/// The overlay used to come down the moment the last file went, leaving the
/// screen to settle four hundred duplicate groups and re-read them with nothing
/// on screen to say so. On a real library that is ten to twenty seconds during
/// which the rows just deleted sit there looking undeleted and the window looks
/// hung.
///
/// <para>An ordering, which is exactly the kind of thing that regresses without
/// anybody noticing: everything still works, it just looks broken for a while.
/// So it is pinned here rather than left to be noticed again.</para>
/// </remarks>
public sealed class DeleteOverlayTests : IDisposable
{
    private readonly string _root;
    private readonly string _photos;
    private readonly GalleryDbContext _db;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _viewModel;

    public DeleteOverlayTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-overlay-{Guid.NewGuid():N}");
        _photos = Path.Combine(_root, "photos");
        string library = Path.Combine(_root, "library");
        Directory.CreateDirectory(_photos);
        Directory.CreateDirectory(library);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(library, "index.db")}")
                .Options);
        _db.Database.Migrate();

        var workingFolder = new WorkingFolder(library);
        workingFolder.EnsureCreated();
        var thumbnails = new FileSystemThumbnailStore(workingFolder);

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _photos });
        _db.Assets.Add(new Asset
        {
            PhotoSourceId = 1,
            RelativePath = "a.jpg",
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            Length = 1024,
            ModifiedUtc = DateTime.UtcNow,
        });
        _db.SaveChanges();

        File.WriteAllText(Path.Combine(_photos, "a.jpg"), "not really a photograph");

        // A real container, so the view-model resolves the real handler the way
        // it does in the app. Only the two things that touch the outside world
        // are stubbed.
        _services = new ServiceCollection()
            .AddSingleton<IAssetRepository>(new SqliteAssetRepository(_db))
            .AddSingleton<IThumbnailStore>(thumbnails)
            .AddSingleton<IOriginalFile>(new StubOriginalFile())
            .AddSingleton<ISourceAvailability>(new AlwaysReachable())
            .AddScoped<RemovePhotoHandler>()
            .BuildServiceProvider();

        var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();

        _viewModel = new MainViewModel(
            scopeFactory,
            new GalleryViewModel(scopeFactory, thumbnails),
            new PeopleViewModel(scopeFactory, thumbnails),
            new DuplicatesViewModel(scopeFactory, thumbnails),
            thumbnails,
            new SilentLog());
    }

    /// <summary>
    /// The bug, stated as a test: the overlay is still up while the screen
    /// catches up.
    /// </summary>
    [Fact]
    public async Task Delete_KeepsTheOverlayUpWhileTheScreenCatchesUp()
    {
        bool overlayUp = false;
        string status = string.Empty;

        PhotoRemovalResult result = await _viewModel.DeletePhotosAsync(
            [Photo()],
            _ =>
            {
                overlayUp = _viewModel.IsOverlayVisible;
                status = _viewModel.OverlayStatus;
                return Task.CompletedTask;
            });

        Assert.Equal(1, result.Deleted);
        Assert.True(overlayUp, "the overlay came down before the screen had caught up");

        // And it says what it is doing, rather than sitting on the last file's
        // name as though that delete were still running.
        Assert.Contains("putting the list back together", status, StringComparison.Ordinal);

        // Down again once everything is finished.
        Assert.False(_viewModel.IsOverlayVisible);
    }

    /// <summary>
    /// Stop is not offered once the files have gone.
    /// </summary>
    /// <remarks>
    /// Nothing in that phase can be stopped, and a button that can be pressed
    /// and does nothing is a worse answer than one that is plainly unavailable.
    /// </remarks>
    [Fact]
    public async Task Delete_StopsOfferingToStopOnceTheFilesHaveGone()
    {
        bool couldStopWhileSettling = true;
        bool overlayUpWhileSettling = false;

        await _viewModel.DeletePhotosAsync(
            [Photo()],
            _ =>
            {
                couldStopWhileSettling = _viewModel.CanStopPass;
                overlayUpWhileSettling = _viewModel.IsOverlayVisible;
                return Task.CompletedTask;
            });

        // Both, so this cannot pass for the wrong reason. Stop is unavailable
        // whenever the overlay is down, and an overlay that had already come
        // down would satisfy the first assertion while proving nothing.
        Assert.True(overlayUpWhileSettling);
        Assert.False(couldStopWhileSettling, "Stop was still offered after the files had gone");
    }

    /// <summary>
    /// A screen that fails to catch up is not a failed deletion.
    /// </summary>
    /// <remarks>
    /// The photographs did go. Reporting nothing deleted because the list could
    /// not be rebuilt would be a lie about the disk, and would leave the caller
    /// unable to settle the groups it just emptied.
    /// </remarks>
    [Fact]
    public async Task Delete_StillReportsWhatWentWhenTheScreenCannotCatchUp()
    {
        PhotoRemovalResult result = await _viewModel.DeletePhotosAsync(
            [Photo()],
            _ => throw new IOException("the index is busy"));

        Assert.Equal(1, result.Deleted);
        Assert.False(_viewModel.IsOverlayVisible);
        Assert.False(_viewModel.IsSettling);
    }

    /// <summary>Deleting with no callback behaves as it always did.</summary>
    [Fact]
    public async Task Delete_WithNothingToSettleFinishesWithTheOverlayDown()
    {
        PhotoRemovalResult result = await _viewModel.DeletePhotosAsync([Photo()]);

        Assert.Equal(1, result.Deleted);
        Assert.False(_viewModel.IsOverlayVisible);
        Assert.False(_viewModel.IsSettling);
    }

    /// <summary>
    /// The same cover for a decision that deletes nothing.
    /// </summary>
    /// <remarks>
    /// "Keep them all" settles a group in one statement and then spends the same
    /// ten to twenty seconds reading every group back. It had no overlay at all,
    /// so the group sat on screen unchanged and the click looked ignored.
    /// </remarks>
    [Fact]
    public async Task UnderOverlay_CoversWorkThatChangesNothingOnDisk()
    {
        bool overlayUp = false;
        bool couldStop = true;
        bool indeterminate = false;

        await _viewModel.UnderOverlayAsync(
            "Keeping every copy",
            "putting the list back together...",
            () =>
            {
                overlayUp = _viewModel.IsOverlayVisible;
                couldStop = _viewModel.CanStopPass;
                indeterminate = _viewModel.OverlayIsIndeterminate;
                return Task.CompletedTask;
            });

        Assert.True(overlayUp, "the work ran with nothing on screen");
        Assert.False(couldStop, "Stop was offered for work that cannot be stopped");
        Assert.True(indeterminate, "a bar that measures nothing should not pretend to");

        Assert.False(_viewModel.IsOverlayVisible);
        Assert.True(_viewModel.IsIdle);
    }

    /// <summary>
    /// A screen that throws does not leave the window covered for ever.
    /// </summary>
    [Fact]
    public async Task UnderOverlay_ComesDownEvenWhenTheWorkFails()
    {
        await Assert.ThrowsAsync<IOException>(() => _viewModel.UnderOverlayAsync(
            "Keeping every copy",
            "putting the list back together...",
            () => throw new IOException("the index is busy")));

        Assert.False(_viewModel.IsOverlayVisible);
        Assert.False(_viewModel.IsTidying);
        Assert.True(_viewModel.IsIdle);
    }

    private PhotoToRemove Photo() =>
        new(
            AssetId: 1,
            FileName: "a.jpg",
            FullPath: Path.Combine(_photos, "a.jpg"),
            SourceRoot: _photos,
            Faces: 0,
            Names: 0,
            Recoverable: true);

    private sealed class StubOriginalFile : IOriginalFile
    {
        public bool GoesToRecycleBin(string fullPath) => true;

        public bool Delete(string fullPath)
        {
            File.Delete(fullPath);
            return true;
        }
    }

    private sealed class AlwaysReachable : ISourceAvailability
    {
        public bool CanReach(string sourceRoot) => true;
    }

    private sealed class SilentLog : IActivityLog
    {
        public void Append(string line)
        {
        }
    }

    public void Dispose()
    {
        _services.Dispose();
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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;
using PhotoGallery.Tests.Sharing;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Adding a folder asks, before its first scan, what lets that scan take the
/// pictures the other computers have already made.
/// </summary>
/// <remarks>
/// Found on a new laptop: the first folder was scanned the moment it was added,
/// before any sync folder was chosen or any folder paired, so it spent hours
/// making every picture the other laptop had already made. Against the real
/// service graph, because what is under test is the order things happen in.
/// </remarks>
public sealed class AddFolderAsksAboutOtherComputersTests : IDisposable
{
    private readonly string _root;
    private readonly string _photos;
    private readonly string _shared;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;
    private readonly AnsweringQuestions _questions = new();

    public AddFolderAsksAboutOtherComputersTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-add-folder-{Guid.NewGuid():N}");
        string library = Path.Combine(_root, "library");
        _photos = Path.Combine(_root, "photos");
        _shared = Path.Combine(_root, "shared");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(_photos);
        Directory.CreateDirectory(_shared);

        _services = new ServiceCollection()
            .AddPhotoGalleryInfrastructure(library)
            .AddPhotoGalleryHandlers()
            .BuildServiceProvider();

        using (IServiceScope scope = _services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<GalleryDbContext>().Database.Migrate();
        }

        var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
        var thumbnails = _services.GetRequiredService<IThumbnailStore>();

        _main = new MainViewModel(
            scopeFactory,
            new GalleryViewModel(scopeFactory, thumbnails),
            new PeopleViewModel(scopeFactory, thumbnails),
            new DuplicatesViewModel(scopeFactory, thumbnails),
            thumbnails,
            new FileActivityLog(new WorkingFolder(library)))
        {
            AddFolderQuestions = _questions,
        };
    }

    [Fact]
    public async Task TheFirstFolder_WithNoSyncFolder_IsOfferedOneBeforeItIsScanned()
    {
        await AddAsync(_photos);

        Assert.Equal(["choose a sync folder"], _questions.Asked);
        Assert.NotNull(Source().LastScanUtc);
    }

    /// <summary>
    /// Said to be the same folder, the new folder is filed under the other
    /// computer's identity before its first scan, so that scan's take matches.
    /// </summary>
    /// <remarks>
    /// Taken rather than linked: a link settles both on the lower id, which is
    /// the new folder's own half the time, and the other computer's pictures
    /// stay under its id until it next syncs - so the first scan would take
    /// nothing.
    /// </remarks>
    [Fact]
    public async Task AFolderAnotherComputerKeeps_TakesItsIdentityBeforeItsFirstScan()
    {
        using Library mum = AnotherComputerKeeping(_photos);
        _questions.SyncFolder = _shared;

        await AddAsync(_photos);

        Assert.Equal(["choose a sync folder", "same folder as Mum's laptop"], _questions.Asked);
        Assert.Equal(mum.SharedSourceId, Source().SharedId);
        Assert.Equal(0, Pairings());
        Assert.NotNull(Source().LastScanUtc);
    }

    [Fact]
    public async Task SayingNo_LeavesTheFoldersApart()
    {
        using Library mum = AnotherComputerKeeping(_photos);
        _questions.SyncFolder = _shared;
        _questions.Same = false;

        await AddAsync(_photos);

        Assert.Contains("same folder as Mum's laptop", _questions.Asked);
        Assert.NotEqual(mum.SharedSourceId, Source().SharedId);
    }

    /// <summary>
    /// Stop pressed while the sync folder is being asked stops: nothing is
    /// asked and the folder is not scanned.
    /// </summary>
    /// <remarks>
    /// The check runs under the scan's overlay and Stop button. It used to have
    /// no cancellation, so Stop said "stopping..." and the scan began anyway.
    /// </remarks>
    [Fact]
    public async Task StopWhileTheSyncFolderIsAsked_StopsBeforeTheScan()
    {
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.OverlayTitle)
                && _main.OverlayTitle == "Checking the sync folder")
            {
                _main.CancelPassCommand.Execute(null);
            }
        };

        await AddAsync(_photos);

        Assert.Empty(_questions.Asked);
        Assert.Null(Source().LastScanUtc);
        Assert.False(_main.IsRefreshing);
    }

    [Fact]
    public async Task ASecondFolder_IsNotOfferedASyncFolderAgain()
    {
        await AddAsync(_photos);
        _questions.Asked.Clear();

        string more = Path.Combine(_root, "more");
        Directory.CreateDirectory(more);
        await AddAsync(more);

        Assert.Empty(_questions.Asked);
    }

    /// <summary>Another laptop that keeps this folder and has published it.</summary>
    private Library AnotherComputerKeeping(string folder)
    {
        var mum = new Library(Path.Combine(_root, "mum"), "Mum's laptop", Guid.NewGuid());
        mum.SharesThrough(_shared);
        mum.Reaches(folder);
        mum.Publishing.HandleAsync().GetAwaiter().GetResult();
        return mum;
    }

    private async Task AddAsync(string folder)
    {
        _main.NewSourcePath = folder;
        await _main.AddSourceCommand.ExecuteAsync(null);
    }

    private T Read<T>(Func<GalleryDbContext, T> query)
    {
        using IServiceScope scope = _services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<GalleryDbContext>());
    }

    private int Pairings() => Read(db => db.PairedSources.Count());

    private PhotoGallery.Domain.Library.PhotoSource Source() =>
        Read(db => db.PhotoSources.AsNoTracking().Single(source => source.Path == _photos));

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // SQLite pools its connections; a temp folder left behind is not a
            // failed test.
        }
    }

    /// <summary>Answers as the test says, and writes down what it was asked.</summary>
    private sealed class AnsweringQuestions : IAddFolderQuestions
    {
        public List<string> Asked { get; } = [];

        /// <summary>The folder chosen when offered one; null declines.</summary>
        public string? SyncFolder { get; set; }

        /// <summary>The answer to "is this the same folder?".</summary>
        public bool Same { get; set; } = true;

        public string? ChooseSyncFolderFirst()
        {
            Asked.Add("choose a sync folder");
            return SyncFolder;
        }

        public bool IsSameFolder(PairingProposal proposal)
        {
            Asked.Add($"same folder as {proposal.MachineName}");
            return Same;
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Albums;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.Sharing;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The picture viewer is drawn over the content area rather than inside Library,
/// so nothing about leaving the section closes it on its own - it stayed on top
/// of whatever the next section loaded, and both were visible at once.
/// </summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    public MainViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-main-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        // An empty provider is enough: closing the viewer resolves nothing, and
        // the sections used here are the ones that do not load the grid.
        _services = new ServiceCollection().BuildServiceProvider();
        IServiceScopeFactory scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();

        var store = new FileSystemThumbnailStore(workingFolder);
        _main = new MainViewModel(
            scopeFactory,
            new GalleryViewModel(scopeFactory, store),
            new PeopleViewModel(scopeFactory, store),
            new DuplicatesViewModel(scopeFactory, store),
            store,
            new FileActivityLog(workingFolder));
    }

    [Fact]
    public void ChangingSection_ClosesTheOpenPicture()
    {
        OpenAPicture();
        Assert.True(_main.Gallery.IsViewerOpen, "the viewer must be open for this to prove anything");

        _main.SelectedSection = SectionNamed("people");

        Assert.False(_main.Gallery.IsViewerOpen);
        Assert.Null(_main.Gallery.OpenPicture);
    }

    [Fact]
    public void ChangingSection_WithNothingOpenDoesNotThrow()
    {
        // The hook runs on every section change, including before a picture has
        // ever been opened.
        _main.SelectedSection = SectionNamed("duplicates");

        Assert.False(_main.Gallery.IsViewerOpen);
    }

    /// <summary>
    /// The way out of the notice is named for what it is getting out of.
    /// </summary>
    /// <remarks>
    /// "Not now" is the right word for declining a question, because the offer
    /// stands and will be made again when there is something newer. It is the
    /// wrong word entirely for a report of work already done: a finished sync
    /// said "5 photos moved. You're up to date." and then offered "Not
    /// now", as though the move might be taken back.
    /// </remarks>
    [Fact]
    public void TheNoticeIsDeclinedWhenItAsksAndDismissedWhenItReports()
    {
        _main.Ask("Mum's laptop has answers.", "Take them now", () => Task.CompletedTask);

        Assert.True(_main.NoticeAsks);
        Assert.Equal("Not now", _main.NoticeDismiss);
        Assert.Contains("offered again", _main.NoticeDismissTip, StringComparison.Ordinal);

        _main.Say("5 photos moved. You're up to date.");

        Assert.False(_main.NoticeAsks);
        Assert.Equal("Dismiss", _main.NoticeDismiss);
        Assert.DoesNotContain("offered again", _main.NoticeDismissTip, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pressing the section you are already on takes you up a level.
    /// </summary>
    /// <remarks>
    /// Standing inside an album, pressing Albums did nothing at all: the row was
    /// already selected, so the command returned early and the only way back to
    /// the wall was a small arrow above the photographs. The section you are on
    /// is the one row that has nowhere else to take you, so it takes you up -
    /// which is what a tab pressed twice does everywhere else.
    /// </remarks>
    [Fact]
    public void PressingTheSectionYouAreOn_GoesBackToTheTopOfIt()
    {
        ActivitySection albums = SectionNamed("albums");
        _main.SelectedSection = albums;

        _main.Albums.OpenCommand.Execute(AnAlbum());
        Assert.True(_main.Albums.HasSelected);

        _main.SelectSectionCommand.Execute(albums);

        Assert.False(_main.Albums.HasSelected);
        Assert.Same(albums, _main.SelectedSection);
    }

    /// <summary>And a section with nothing open is left exactly as it was.</summary>
    [Fact]
    public void PressingTheSectionYouAreOn_WithNothingOpenChangesNothing()
    {
        ActivitySection albums = SectionNamed("albums");
        _main.SelectedSection = albums;

        _main.SelectSectionCommand.Execute(albums);

        Assert.False(_main.Albums.HasSelected);
        Assert.Same(albums, _main.SelectedSection);
    }

    /// <summary>
    /// A tile with no rendition name, so opening it touches no file: the viewer
    /// opening at all is what matters here, not what it managed to draw.
    /// </summary>
    private void OpenAPicture() =>
        _main.Gallery.OpenPhotoCommand.Execute(new GalleryTile(new GalleryItem(
            1,
            @"holiday\P1070491.JPG",
            "P1070491.JPG",
            "holiday",
            @"C:\pictures\holiday\P1070491.JPG",
            null,
            null,
            new DateTime(2012, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            0,
            AssetKind.Photo)));

    /// <summary>An album with no cover, so opening it touches no file.</summary>
    private static AlbumItem AnAlbum() =>
        new(
            new AlbumSummary(
                7,
                "Zoo 2026",
                DateTime.UnixEpoch,
                DateTime.UnixEpoch,
                AlbumKind.Trip,
                AlbumOrigin.Made,
                3,
                CoverThumbnailName: null),
            Cover: null);

    private ActivitySection SectionNamed(string key) =>
        _main.TopSections.Single(section => section.Key == key);

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}

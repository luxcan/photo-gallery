using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
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

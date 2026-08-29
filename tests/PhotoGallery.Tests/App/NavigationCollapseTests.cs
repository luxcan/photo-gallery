using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.Sharing;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// A narrow window folds the side nav on its own, which is not the same as the
/// user asking for it. Confusing the two means a resize made for an unrelated
/// reason quietly overwrites a deliberate choice - and that one session on a
/// small screen decides how the next one opens.
/// </summary>
public sealed class NavigationCollapseTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    public NavigationCollapseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-nav-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        // An empty provider is enough for everything except saving, which the
        // one test that folds by hand deliberately lets fail.
        _services = new ServiceCollection().BuildServiceProvider();
        IServiceScopeFactory scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();

        var store = new FileSystemThumbnailStore(workingFolder);
        _main = new MainViewModel(
            scopeFactory,
            new GalleryViewModel(scopeFactory, store),
            new PeopleViewModel(scopeFactory, store),
            new DuplicatesViewModel(scopeFactory, store),
            store,
            new FileActivityLog(workingFolder),
            new DirectSharing(scopeFactory));
    }

    [Fact]
    public void TheThreshold_IsTheWidthBelowIt()
    {
        Assert.True(NavLayout.IsNarrow(NavLayout.NarrowWindowWidth - 1));
        Assert.False(NavLayout.IsNarrow(NavLayout.NarrowWindowWidth));
    }

    [Fact]
    public void OpeningANarrowWindow_FoldsTheNav()
    {
        _main.AdaptNavigationToWidth(900d);

        Assert.True(_main.IsNavCollapsed);
    }

    [Fact]
    public void OpeningAWideWindow_LeavesTheNavOpen()
    {
        _main.AdaptNavigationToWidth(1600d);

        Assert.False(_main.IsNavCollapsed);
    }

    [Fact]
    public void ResizingWithinOneBand_ChangesNothing()
    {
        _main.AdaptNavigationToWidth(1600d);
        Fold();

        _main.AdaptNavigationToWidth(1400d);
        _main.AdaptNavigationToWidth(1200d);

        Assert.True(
            _main.IsNavCollapsed,
            "dragging the edge about above the threshold is not an opinion about the nav");
    }

    [Fact]
    public void DraggingBelowTheThreshold_FoldsTheNavWithoutRecordingAChoice()
    {
        _main.AdaptNavigationToWidth(1600d);
        Assert.False(_main.IsNavCollapsed);

        _main.AdaptNavigationToWidth(900d);
        Assert.True(_main.IsNavCollapsed);

        _main.AdaptNavigationToWidth(1600d);
        Assert.False(
            _main.IsNavCollapsed,
            "nobody asked for that fold, so leaving the narrow window undoes it");
    }

    [Fact]
    public void AFoldChosenOnAWideWindow_SurvivesAShrinkAndAGrow()
    {
        _main.AdaptNavigationToWidth(1600d);
        Fold();

        _main.AdaptNavigationToWidth(900d);
        _main.AdaptNavigationToWidth(1600d);

        Assert.True(_main.IsNavCollapsed);
    }

    [Fact]
    public void TheChevron_BeatsTheThresholdWhileTheWindowIsNarrow()
    {
        _main.AdaptNavigationToWidth(900d);
        Assert.True(_main.IsNavCollapsed);

        Fold();

        Assert.False(
            _main.IsNavCollapsed,
            "a narrow window folds the nav, but it does not get to keep it folded");
    }

    [Fact]
    public void AStoredFold_IsPutBackAndIsNotWrittenStraightOut()
    {
        // RestoreNavigation must not save: the provider here has no handler in
        // it, so a write would throw rather than quietly do nothing.
        _main.RestoreNavigation(collapsed: true);

        Assert.True(_main.IsNavCollapsed);

        // And it counts as the user's own, so a narrow window round trip keeps it.
        _main.AdaptNavigationToWidth(1600d);
        Assert.True(_main.IsNavCollapsed);
    }

    /// <summary>
    /// Folds the nav the way the chevron does, and swallows the save.
    /// </summary>
    /// <remarks>
    /// The fixture's provider holds no handler, so remembering the fold throws
    /// where the real app would write a row. The view model catches that and
    /// logs it, which is exactly the behaviour being relied on here - the fold
    /// itself has already happened by then.
    /// </remarks>
    private void Fold() => _main.ToggleNavigationCommand.Execute(null);

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

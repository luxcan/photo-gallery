using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Collections;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Which of the two tabs the albums screen opens on, and what pressing the other
/// one shows.
/// </summary>
/// <remarks>
/// The screen opened on Suggested, which is a queue of questions - keep this
/// occasion, or throw it away - and it was the first thing anybody saw who had
/// come to look at an album they had made themselves. Their own albums are the
/// ones they named, and the ones no scan ever changes, so those are what the
/// screen opens on and they are the first tab.
///
/// <para>Pressing a tab also opened whichever album happened to be first on it.
/// That is what a list beside a grid wanted; this screen is two states - a wall
/// of albums, or one album open - and answering "show me the suggestions" by
/// walking into one of them is not an answer. Both are pinned here because
/// neither is visible from anywhere else: an order in markup and a default in a
/// field, both of which a fully green suite was happy with.</para>
/// </remarks>
public sealed class AlbumTabsTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly ServiceProvider _services;
    private readonly ICollectionRepository _collections;
    private readonly CollectionsViewModel _albums;

    public AlbumTabsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-album-tabs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _collections = new SqliteCollectionRepository(_db);

        _services = new ServiceCollection()
            .AddSingleton(_collections)
            .BuildServiceProvider();

        _albums = new CollectionsViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    /// <summary>
    /// What is on screen before anybody has pressed anything.
    /// </summary>
    [Fact]
    public void TheScreenOpensOnTheAlbumsTheUserMade()
    {
        Assert.True(_albums.ShowMine);
        Assert.False(_albums.IsShowingSuggested);
        Assert.Same(_albums.Mine, _albums.Showing);
    }

    /// <summary>
    /// The empty state matches the tab that is actually showing.
    /// </summary>
    /// <remarks>
    /// A first run has neither list filled, so this sentence is the whole screen.
    /// Telling somebody to scan for suggestions while they are looking at the tab
    /// where New album lives would be the wrong instruction on the wrong tab.
    /// </remarks>
    [Fact]
    public void AnEmptyScreenSaysWhatToDoOnTheTabItIsShowing()
    {
        Assert.Contains("New album", _albums.EmptyMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pressing a tab shows that tab's wall, with nothing open.
    /// </summary>
    /// <remarks>
    /// Written from the suggestions back to the user's own, because that is the
    /// direction with an album on the far side to be wrongly opened: it is the
    /// tab that has one.
    /// </remarks>
    [Fact]
    public async Task ChangingTab_ShowsThatWall_RatherThanOpeningTheFirstAlbumOnIt()
    {
        await _collections.CreateAsync("Genting");
        await _albums.ReloadAsync();

        _albums.IsShowingSuggested = true;
        _albums.ShowMine = true;

        Assert.Single(_albums.Showing);
        Assert.False(_albums.HasSelected);
    }

    /// <summary>
    /// The tabs are in the order the screen is read in.
    /// </summary>
    /// <remarks>
    /// A binding cannot say this and no view model can be asked it - which of two
    /// radio buttons comes first is markup, and markup is where it silently goes
    /// back to being the other way round.
    /// </remarks>
    [Fact]
    public void TheUsersOwnAlbumsAreTheFirstTab()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        int mine = window.IndexOf("Content=\"Your albums\"", StringComparison.Ordinal);
        int suggested = window.IndexOf("Content=\"Suggested\"", StringComparison.Ordinal);

        Assert.True(mine >= 0, "The tab for the user's own albums has been renamed or removed.");
        Assert.True(suggested >= 0, "The tab of proposals has been renamed or removed.");
        Assert.True(suggested > mine, "Suggested is the second tab, not the first.");
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

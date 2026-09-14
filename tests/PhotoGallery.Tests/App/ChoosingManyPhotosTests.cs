using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Albums;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Choosing several photographs on the wall, to move or to delete together.
/// </summary>
/// <remarks>
/// Tidying a holiday meant opening and closing the viewer a few hundred times,
/// because every way a picture could leave the wall went through it one at a
/// time. Both of the things this does were already built for many photographs -
/// the deleting funnel and the album write have taken lists all along - so what
/// is argued out here is the choosing: what a click means while the mode is on,
/// and what happens to a choice the wall stops being able to honour.
/// </remarks>
public sealed class ChoosingManyPhotosTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly List<ServiceProvider> _built = [];
    private readonly string _root;
    private readonly GalleryViewModel _gallery;

    public ChoosingManyPhotosTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-choosing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    /// <summary>With the mode off, a click still opens the picture.</summary>
    /// <remarks>
    /// The rule this screen was built on. Choosing several cannot be a different
    /// click on the same tile, or one of the two has to lose.
    /// </remarks>
    [Fact]
    public void WithTheModeOff_AClickOpensThePhotograph()
    {
        GalleryTile tile = Tile(1, "a.jpg");

        _gallery.OpenPhotoCommand.Execute(tile);

        Assert.True(_gallery.IsViewerOpen);
        Assert.False(tile.IsChosen);
        Assert.Equal(0, _gallery.ChosenCount);
    }

    /// <summary>With it on, the same click ticks it instead.</summary>
    [Fact]
    public void WithTheModeOn_AClickTicksThePhotograph()
    {
        GalleryTile tile = Tile(1, "a.jpg");
        _gallery.IsChoosing = true;

        _gallery.OpenPhotoCommand.Execute(tile);

        Assert.False(_gallery.IsViewerOpen);
        Assert.True(tile.IsChosen);
        Assert.Equal(1, _gallery.ChosenCount);
        Assert.Equal("1 chosen", _gallery.ChosenSummary);
    }

    /// <summary>And clicking it again unticks it.</summary>
    [Fact]
    public void ClickingATickedPhotographAgainUnticksIt()
    {
        GalleryTile tile = Tile(1, "a.jpg");
        _gallery.IsChoosing = true;

        _gallery.OpenPhotoCommand.Execute(tile);
        _gallery.OpenPhotoCommand.Execute(tile);

        Assert.False(tile.IsChosen);
        Assert.Equal(0, _gallery.ChosenCount);
        Assert.False(_gallery.HasChosen);
    }

    /// <summary>The delete button carries the count, in both numbers.</summary>
    /// <remarks>
    /// The last place the number can be noticed before the question is asked.
    /// </remarks>
    [Fact]
    public void TheDeleteButtonSaysHowManyItWouldDestroy()
    {
        _gallery.IsChoosing = true;

        _gallery.OpenPhotoCommand.Execute(Tile(1, "a.jpg"));
        Assert.Equal("Delete 1 photo", _gallery.DeleteChosenCaption);

        _gallery.OpenPhotoCommand.Execute(Tile(2, "b.jpg"));
        Assert.Equal("Delete 2 photos", _gallery.DeleteChosenCaption);
    }

    /// <summary>Leaving the mode forgets what was ticked.</summary>
    /// <remarks>
    /// A wall that looks untouched and still has forty photographs spoken for
    /// would make the next press of Delete a question about pictures nobody
    /// could see were chosen.
    /// </remarks>
    /// <remarks>
    /// Asserted on the count rather than on the tile handed in. The set of ids
    /// is what this holds; the mark on a picture is a mirror of it, put back
    /// onto whatever tiles the wall has at the time - so a tile that was never
    /// on a wall is not something the model has an opinion about.
    /// </remarks>
    [Fact]
    public void LeavingTheModeForgetsWhatWasTicked()
    {
        _gallery.IsChoosing = true;
        _gallery.OpenPhotoCommand.Execute(Tile(1, "a.jpg"));

        _gallery.StopChoosingCommand.Execute(null);

        Assert.False(_gallery.IsChoosing);
        Assert.Equal(0, _gallery.ChosenCount);
        Assert.False(_gallery.HasChosen);
        Assert.Equal("None chosen", _gallery.ChosenSummary);
    }

    /// <summary>Clearing unticks everything and stays in the mode.</summary>
    [Fact]
    public void ClearingUnticksEverythingAndStaysInTheMode()
    {
        _gallery.IsChoosing = true;
        _gallery.OpenPhotoCommand.Execute(Tile(1, "a.jpg"));

        _gallery.ChooseNothingCommand.Execute(null);

        Assert.True(_gallery.IsChoosing);
        Assert.Equal(0, _gallery.ChosenCount);
        Assert.False(_gallery.HasChosen);
    }

    /// <summary>Nothing is ticked to begin with, and the strip says so.</summary>
    [Fact]
    public void NothingIsTickedToBeginWith()
    {
        Assert.False(_gallery.IsChoosing);
        Assert.False(_gallery.HasChosen);
        Assert.Equal("None chosen", _gallery.ChosenSummary);
    }

    /// <summary>
    /// The album sentence names the album and where they came from.
    /// </summary>
    /// <remarks>
    /// A photograph belongs to one album, so putting it somewhere takes it out
    /// of wherever it was. That is nobody's expectation until it happens to
    /// them, so it is said rather than done quietly - and the album is named
    /// because the wall the reader is standing on is the one they just left.
    /// </remarks>
    [Theory]
    [InlineData(1, 0, "Added to Bali")]
    [InlineData(12, 0, "12 photographs added to Bali.")]
    [InlineData(1, 1, "Moved into Bali, out of March 2019")]
    [InlineData(12, 12, "12 photographs moved into Bali, out of March 2019.")]
    [InlineData(12, 5, "12 photographs added to Bali, 5 of them out of March 2019.")]
    public void TheAlbumSentenceNamesTheAlbumAndWhereTheyCameFrom(
        int placed, int moved, string expected)
    {
        var result = new AlbumAddResult(placed, moved, moved == 0 ? [] : ["March 2019"]);

        Assert.Equal(expected, AlbumMoveNotice.For("Bali", placed, result));
    }

    /// <summary>
    /// The wall carries the controls, and the mode decides which are shown.
    /// </summary>
    /// <remarks>
    /// Markup, so no view-model test can see it: every property above can be
    /// right while the strip is bound to none of them, because a WPF binding
    /// that names nothing fails in silence.
    /// </remarks>
    [Fact]
    public void TheStripCarriesTheChoosingControls()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        Assert.Contains("Gallery.IsChoosing", window, StringComparison.Ordinal);
        Assert.Contains("Gallery.ChosenSummary", window, StringComparison.Ordinal);
        Assert.Contains("Gallery.ChooseEverythingCommand", window, StringComparison.Ordinal);
        Assert.Contains("Gallery.ChooseNothingCommand", window, StringComparison.Ordinal);
        Assert.Contains("Gallery.MoveChosenToAlbumCommand", window, StringComparison.Ordinal);
        Assert.Contains("Gallery.DeleteChosenCaption", window, StringComparison.Ordinal);
        Assert.Contains("OnDeleteChosenPhotosClicked", window, StringComparison.Ordinal);
    }

    /// <summary>A ticked photograph is marked on the picture itself.</summary>
    /// <remarks>
    /// On the tile, because the rows virtualise with container recycling - a
    /// mark held by the container would wander onto another photograph the
    /// moment the wall was scrolled.
    /// </remarks>
    [Fact]
    public void ATickedPhotographIsMarkedOnThePicture()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));
        string controls = File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));

        Assert.Contains("ChosenTick", window, StringComparison.Ordinal);
        Assert.Contains("ChosenVeil", window, StringComparison.Ordinal);
        Assert.Contains("IsChosen", window, StringComparison.Ordinal);

        Assert.Contains("x:Key=\"ChosenTick\"", controls, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ChosenVeil\"", controls, StringComparison.Ordinal);
    }

    /// <summary>
    /// Putting a batch in an album is handed to the shell, not done here.
    /// </summary>
    /// <remarks>
    /// The write is quick and the wall rebuilt afterwards is not, so it wants
    /// the overlay - and the overlay is the shell's, for the reason keeping a
    /// duplicate group is the shell's. Done here it would run with the window
    /// live, leaving the moved photographs on screen looking untouched and
    /// every other control still clickable.
    /// </remarks>
    [Fact]
    public async Task PuttingABatchInAnAlbumIsHandedToTheShell()
    {
        GalleryViewModel gallery = WithAlbums(new OneAlbum());

        string? asked = null;
        gallery.PlacingChosen += (_, name) => asked = name;

        gallery.IsChoosing = true;
        gallery.OpenPhotoCommand.Execute(Tile(1, "a.jpg"));
        gallery.OpenPhotoCommand.Execute(Tile(2, "b.jpg"));

        await gallery.MoveChosenToAlbumCommand.ExecuteAsync(null);
        await gallery.Albums.ChooseCommand.ExecuteAsync(gallery.Albums.Choices[0]);

        Assert.Equal("Bali", asked);

        // And nothing was written on the way past: the shell writes, under the
        // overlay, when it calls back.
        Assert.Equal(0, OneAlbum.Added);
    }

    /// <summary>
    /// The picker names how many it is about to place.
    /// </summary>
    [Fact]
    public async Task ThePickerSaysHowManyItIsPlacing()
    {
        GalleryViewModel gallery = WithAlbums(new OneAlbum());

        gallery.IsChoosing = true;
        gallery.OpenPhotoCommand.Execute(Tile(1, "a.jpg"));
        gallery.OpenPhotoCommand.Execute(Tile(2, "b.jpg"));

        await gallery.MoveChosenToAlbumCommand.ExecuteAsync(null);

        Assert.Equal("Put these 2 photographs in an album", gallery.Albums.Prompt);

        // No album to leave, because two photographs may have come from two
        // places - so the way out of one hides itself.
        Assert.False(gallery.Albums.IsInOne);
    }

    /// <summary>
    /// The album picker is hosted where the wall can show it, not only the
    /// viewer.
    /// </summary>
    /// <remarks>
    /// It used to live inside the viewer's own chrome, which is gated on a
    /// photograph being open - so asking a wall of ticked pictures which album
    /// they belong in put a panel on screen that nobody could see. The bug was
    /// invisible to every view-model test: the picker said it was open, and it
    /// was, in a part of the window that was not being drawn.
    /// </remarks>
    [Fact]
    public void ThePickerIsHostedWhereTheWallCanShowIt()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        // One host, so there is one place for it to be wrong.
        Assert.Equal(1, Occurrences(window, "Content=\"{Binding Gallery.Albums}\""));

        int host = window.IndexOf("Content=\"{Binding Gallery.Albums}\"", StringComparison.Ordinal);
        int viewer = window.IndexOf(
            "Visibility=\"{Binding Gallery.IsViewerOpen,", StringComparison.Ordinal);

        Assert.True(host > 0 && viewer > 0, "the markup no longer has both");
        Assert.True(
            host > viewer,
            "the picker is inside the viewer again, so a wall cannot show it");

        // And it is not gated on a photograph being open.
        Assert.Contains(
            "Visibility=\"{Binding Gallery.Albums.IsOpen,", window, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value)
    {
        int found = 0;

        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }

    private GalleryViewModel WithAlbums(IAlbumRepository albums)
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(albums)
            .BuildServiceProvider();

        _built.Add(services);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        return new GalleryViewModel(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    private static GalleryTile Tile(int id, string fileName) =>
        new(new GalleryItem(
            id,
            $@"holiday\{fileName}",
            fileName,
            "holiday",
            $@"C:\pictures\holiday\{fileName}",
            fileName,
            null,
            new DateTime(2012, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            0,
            AssetKind.Photo));

    /// <summary>
    /// One album to choose, and a count of what was actually written.
    /// </summary>
    /// <remarks>
    /// Everything else throws rather than answering quietly, so a path that
    /// reaches the repository by a route these tests did not intend says so.
    /// </remarks>
    private sealed class OneAlbum : IAlbumRepository
    {
        public static int Added { get; private set; }

        public OneAlbum() => Added = 0;

        public Task<IReadOnlyList<AlbumSummary>> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AlbumSummary>>(
            [
                new AlbumSummary(
                    7, "Bali", DateTime.UnixEpoch, DateTime.UnixEpoch,
                    AlbumKind.Trip, AlbumOrigin.Made, 3, CoverThumbnailName: null),
            ]);

        public Task<AlbumAddResult> AddAsync(
            int albumId, IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(assetIds);
            Added += assetIds.Count;

            return Task.FromResult(new AlbumAddResult(assetIds.Count, 0, []));
        }

        public Task<int> CreateAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DatedPhoto>> GetCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<int>>> GetRejectionsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> SaveProposalsAsync(
            IReadOnlyList<ProposedAlbum> proposals,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AlbumSummary?> FindForAssetAsync(
            int assetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<int>> GetMembersAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AlbumRule> GetRuleAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetRuleAsync(
            int albumId, AlbumRule rule, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<int>> SuggestAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AcceptAsync(int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DismissAsync(int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RenameAsync(
            int albumId, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            int albumId,
            IReadOnlyList<int> assetIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RefuseAsync(
            int albumId,
            IReadOnlyList<int> assetIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SetCoverAsync(
            int albumId, int assetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    public void Dispose()
    {
        _services.Dispose();

        foreach (ServiceProvider services in _built)
        {
            services.Dispose();
        }

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

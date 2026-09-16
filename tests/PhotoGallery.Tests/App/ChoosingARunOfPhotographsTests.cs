using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Ticking photographs from the circle drawn on them, and taking a run of them
/// with Shift.
/// </summary>
/// <remarks>
/// Backed by a real wall rather than loose tiles, because a run is measured
/// between two positions on it: the thing under test is exactly the part that
/// loose tiles cannot have.
///
/// <para>The rules argued out here are the ones that look like details until
/// somebody has forty photographs ticked. A run <em>adds</em>, so one already
/// ticked inside it is not switched off by the gesture meant to include it. The
/// anchor stays where it was, so stretching a run twice does not walk down the
/// wall. And it is the tile that is remembered, not its index - every rebuild
/// hands out fresh ones.</para>
/// </remarks>
public sealed class ChoosingARunOfPhotographsTests : IDisposable
{
    private const int Pictures = 40;

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly GalleryViewModel _gallery;

    public ChoosingARunOfPhotographsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        string database = Path.Combine(_root, "index.db");
        Seed(database);

        _services = new ServiceCollection()
            .AddDbContext<GalleryDbContext>(
                options => options.UseSqlite($"Data Source={database}"))
            .AddScoped<IGalleryReader, SqliteGalleryReader>()
            .AddTransient<QueryGalleryHandler>()
            .BuildServiceProvider();

        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    /// <summary>
    /// Ticking one photograph is how choosing starts.
    /// </summary>
    /// <remarks>
    /// It used to start at a pill at the far end of the strip, and a mode you
    /// have to find a button for before any picture will answer is a step
    /// nobody guesses at - it came back as the app having no way to choose
    /// several at all.
    /// </remarks>
    [Fact]
    public async Task TickingAPhotographIsHowChoosingStarts()
    {
        IReadOnlyList<GalleryTile> wall = await WallAsync();

        Assert.False(_gallery.IsChoosing);

        _gallery.ChoosePhotoCommand.Execute(wall[3]);

        Assert.True(_gallery.IsChoosing);
        Assert.True(wall[3].IsChosen);
        Assert.Equal(1, _gallery.ChosenCount);
    }

    /// <summary>And pressing the same circle again unticks it.</summary>
    [Fact]
    public async Task TickingTheSamePhotographAgainUnticksIt()
    {
        IReadOnlyList<GalleryTile> wall = await WallAsync();

        _gallery.ChoosePhotoCommand.Execute(wall[3]);
        _gallery.ChoosePhotoCommand.Execute(wall[3]);

        Assert.False(wall[3].IsChosen);
        Assert.Equal(0, _gallery.ChosenCount);

        // Still choosing: unticking the last one is not the way out of the mode.
        // That is Cancel, and it says so.
        Assert.True(_gallery.IsChoosing);
    }

    /// <summary>Shift takes everything between the two, both ends included.</summary>
    [Fact]
    public async Task ShiftTakesTheRunBetweenTheTwo()
    {
        IReadOnlyList<GalleryTile> wall = await WallAsync();

        _gallery.ChoosePhoto(wall[4], extend: false);
        _gallery.ChoosePhoto(wall[9], extend: true);

        Assert.Equal(6, _gallery.ChosenCount);
        Assert.All(wall.Skip(4).Take(6), tile => Assert.True(tile.IsChosen));
        Assert.False(wall[3].IsChosen);
        Assert.False(wall[10].IsChosen);
    }

    /// <summary>Backwards too: a run has no direction.</summary>
    [Fact]
    public async Task TheRunIsTheSameTakenBackwards()
    {
        IReadOnlyList<GalleryTile> wall = await WallAsync();

        _gallery.ChoosePhoto(wall[9], extend: false);
        _gallery.ChoosePhoto(wall[4], extend: true);

        Assert.Equal(6, _gallery.ChosenCount);
        Assert.All(wall.Skip(4).Take(6), tile => Assert.True(tile.IsChosen));
    }

    /// <summary>
    /// A run adds; it does not toggle what it passes over.
    /// </summary>
    /// <remarks>
    /// Toggling would mean the gesture that was meant to include a photograph
    /// silently dropped it, which is the worst possible answer: the wall says
    /// six and the delete asks about five.
    /// </remarks>
    [Fact]
    public async Task ARunLeavesWhatWasAlreadyTickedInsideItTicked()
    {
        IReadOnlyList<GalleryTile> wall = await WallAsync();

        _gallery.ChoosePhoto(wall[6], extend: false);
        Assert.True(wall[6].IsChosen);

        _gallery.ChoosePhoto(wall[4], extend: false);
        _gallery.ChoosePhoto(wall[9], extend: true);

        Assert.Equal(6, _gallery.ChosenCount);
        Assert.True(wall[6].IsChosen);
    }

    /// <summary>
    /// Stretching a run twice measures from the same place both times.
    /// </summary>
    /// <remarks>
    /// The anchor stays where the plain click put it. Moving it to the end of
    /// each run would make a second Shift-click start from wherever the first
    /// one finished, so reaching past a run by one more would take a run of one.
    /// </remarks>
    [Fact]
    public async Task StretchingARunTwiceMeasuresFromTheSamePhotograph()
    {
        IReadOnlyList<GalleryTile> wall = await WallAsync();

        _gallery.ChoosePhoto(wall[4], extend: false);
        _gallery.ChoosePhoto(wall[6], extend: true);
        _gallery.ChoosePhoto(wall[9], extend: true);

        Assert.Equal(6, _gallery.ChosenCount);
        Assert.All(wall.Skip(4).Take(6), tile => Assert.True(tile.IsChosen));
    }

    /// <summary>
    /// Shift with nothing ticked yet is simply a tick.
    /// </summary>
    /// <remarks>
    /// There is no run to take from nowhere, and the alternative - everything
    /// from the top of the wall to here - is forty thousand photographs on one
    /// stray keypress.
    /// </remarks>
    [Fact]
    public async Task ShiftWithNothingTickedYetTicksTheOne()
    {
        IReadOnlyList<GalleryTile> wall = await WallAsync();

        _gallery.ChoosePhoto(wall[7], extend: true);

        Assert.Equal(1, _gallery.ChosenCount);
        Assert.True(wall[7].IsChosen);
    }

    /// <summary>
    /// A run measured from a photograph the wall no longer holds is a tick.
    /// </summary>
    /// <remarks>
    /// The wall can be reloaded between the two clicks - a deletion, a filter,
    /// a search - and the anchor then belongs to a wall that is gone. The tile
    /// is remembered rather than its index precisely so this case can be
    /// recognised at all: an index would still be in range and would quietly
    /// take the wrong run.
    /// </remarks>
    [Fact]
    public async Task AnAnchorTheWallNoLongerHoldsTakesNoRun()
    {
        IReadOnlyList<GalleryTile> wall = await WallAsync();

        _gallery.ChoosePhoto(wall[4], extend: false);

        // The wall again, from scratch: every tile on it is a new object.
        await _gallery.LoadAsync();
        IReadOnlyList<GalleryTile> after = Wall();

        _gallery.ChoosePhoto(after[9], extend: true);

        Assert.True(after[9].IsChosen);
        Assert.False(after[5].IsChosen);
    }

    /// <summary>
    /// Leaving the mode forgets where a run would have been measured from.
    /// </summary>
    /// <remarks>
    /// Otherwise the first Shift-click of the next selection reaches back to a
    /// photograph chosen before the last one was cancelled.
    /// </remarks>
    [Fact]
    public async Task LeavingTheModeForgetsTheAnchor()
    {
        IReadOnlyList<GalleryTile> wall = await WallAsync();

        _gallery.ChoosePhoto(wall[4], extend: false);
        _gallery.StopChoosingCommand.Execute(null);

        _gallery.ChoosePhoto(wall[9], extend: true);

        Assert.Equal(1, _gallery.ChosenCount);
        Assert.True(wall[9].IsChosen);
        Assert.False(wall[5].IsChosen);
    }

    /// <summary>Select all still means every photograph on the wall.</summary>
    /// <remarks>
    /// Worth a wall to say it on: the only test this had was an empty one, which
    /// cannot tell "all" from "none".
    /// </remarks>
    [Fact]
    public async Task SelectAllTakesTheWholeWallAndThenHasNothingLeftToDo()
    {
        IReadOnlyList<GalleryTile> wall = await WallAsync();

        _gallery.IsChoosing = true;
        _gallery.ChooseEverythingCommand.Execute(null);

        Assert.Equal(Pictures, _gallery.ChosenCount);
        Assert.All(wall, tile => Assert.True(tile.IsChosen));
        Assert.True(_gallery.EverythingChosen);
        Assert.False(_gallery.CanChooseEverything);
    }

    /// <summary>
    /// The circle is on the photograph, reveals itself, and is a button.
    /// </summary>
    /// <remarks>
    /// Markup, so no view-model test can see it. All three parts matter: a mark
    /// that is only drawn when already ticked is no way in; a circle burnt into
    /// twenty thousand corners is a permanent blemish; and a Border with a
    /// handler on it would open the photograph as well, because the tile is
    /// itself a button and only a ButtonBase stops the click there.
    /// </remarks>
    [Fact]
    public void TheCircleIsDrawnOnTheTileAndIsTheWayIn()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));
        string controls = File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));

        Assert.Contains(
            "<Button Style=\"{DynamicResource ChosenTick}\"", window, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPhotographTicked\"", window, StringComparison.Ordinal);

        Assert.Contains("x:Key=\"ChosenTick\" TargetType=\"Button\"", controls, StringComparison.Ordinal);

        // Hidden until the pointer is on the picture, or the picture is ticked.
        Assert.Contains("<Setter Property=\"Visibility\" Value=\"Collapsed\" />", controls, StringComparison.Ordinal);
        Assert.Contains(
            "RelativeSource={RelativeSource AncestorType=Button}}\"", controls, StringComparison.Ordinal);
    }

    /// <summary>And the shell reads the modifier, because the view model cannot.</summary>
    [Fact]
    public void TheShellIsWhatReadsTheShiftKey()
    {
        string shell = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml.cs"));

        Assert.Contains("OnPhotographTicked", shell, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Shift", shell, StringComparison.Ordinal);
    }

    /// <summary>A loaded wall, and its tiles in the order they are laid out.</summary>
    private async Task<IReadOnlyList<GalleryTile>> WallAsync()
    {
        _gallery.SetVisibleRows(4);
        await _gallery.LoadAsync();
        _gallery.SetColumns(10);

        Assert.Equal(Pictures, _gallery.TotalCount);

        return Wall();
    }

    private IReadOnlyList<GalleryTile> Wall() =>
        [.. _gallery.Rows.SelectMany(row => row.Tiles)];

    private static void Seed(string database)
    {
        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={database}")
                .Options;

        using var db = new GalleryDbContext(options);
        db.Database.Migrate();
        db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = @"C:\pictures" });

        for (int i = 0; i < Pictures; i++)
        {
            db.Assets.Add(new Asset
            {
                Id = i + 1,
                PhotoSourceId = 1,
                RelativePath = $@"2020\{i:D4}.jpg",
                Length = 1,
                ModifiedUtc = new DateTime(2020, 1, 1).AddMinutes(i),
                CreatedUtc = new DateTime(2020, 1, 1),
                IndexedUtc = new DateTime(2020, 1, 1),
                Kind = AssetKind.Photo,
            });
        }

        db.SaveChanges();
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A thumbnail store may still hold a handle; the folder is temporary.
        }
    }
}

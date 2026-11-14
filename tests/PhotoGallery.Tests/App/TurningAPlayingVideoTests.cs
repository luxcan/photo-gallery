using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Search;
using PhotoGallery.Infrastructure;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.App;

/// <summary>
/// A turn made to a clip is saved like a photograph's, and is still there the
/// next time the clip is played.
/// </summary>
/// <remarks>
/// A turn made while a clip played used to move the film and nothing else: no
/// row recorded it, the poster stayed as it was, and the next open undid it.
/// Against the real service graph, a real rendition and a clip file built in
/// <see cref="TestMp4"/>, because the claim is that the saved path and the
/// player agree, and a fake on either side would only agree with itself.
/// </remarks>
public sealed class TurningAPlayingVideoTests : IDisposable
{
    private const int Clip = 1;
    private const int Photograph = 2;

    private readonly string _root;
    private readonly string _photos;
    private readonly ServiceProvider _services;
    private readonly IThumbnailStore _thumbnails;
    private readonly GalleryViewModel _gallery;
    private string _poster = string.Empty;

    public TurningAPlayingVideoTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-turn-clip-{Guid.NewGuid():N}");
        _photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;

        _services = new ServiceCollection()
            .AddPhotoGalleryInfrastructure(Directory.CreateDirectory(Path.Combine(_root, "library")).FullName)
            .AddPhotoGalleryHandlers()
            .BuildServiceProvider();

        using (IServiceScope scope = _services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<GalleryDbContext>().Database.Migrate();
        }

        _thumbnails = _services.GetRequiredService<IThumbnailStore>();
        Seed();

        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(), _thumbnails);
    }

    [Fact]
    public async Task ATurnWhilePlayingIsSavedAndTurnsTheFilmAtOnce()
    {
        // Filed upside down, which the player does not honour and the file says.
        GalleryTile tile = TileOf(Clip);
        await PlayAsync(tile);
        Assert.Equal(180d, _gallery.PlayingRotation);

        await _gallery.TurnPhotoCommand.ExecuteAsync("90");

        Assert.True(_gallery.IsPlayingVideo);
        Assert.Equal(270d, _gallery.PlayingRotation);
        Assert.Equal(90, await RotationOnRecordAsync());

        // The poster went with it, as a photograph's preview does.
        Assert.Equal((60, 80), PosterSize());
    }

    [Fact]
    public async Task TheTurnIsStillThereOnTheNextPlay()
    {
        GalleryTile tile = TileOf(Clip);
        await PlayAsync(tile);
        await _gallery.TurnPhotoCommand.ExecuteAsync("90");

        // Away and back to the same tile, which is what stepping through the
        // viewer does.
        _gallery.OpenTile = TileOf(Photograph);
        Assert.Equal(0d, _gallery.PlayingRotation);
        await PlayAsync(tile);

        Assert.Equal(270d, _gallery.PlayingRotation);

        // And from the row itself, as a grid read afresh would build it.
        _gallery.OpenTile = TileOf(Photograph);
        await PlayAsync(TileOf(Clip, await RotationOnRecordAsync()));

        Assert.Equal(270d, _gallery.PlayingRotation);
    }

    [Fact]
    public async Task ATurnWhileStoppedTurnsTheLoadedClipToo()
    {
        // Stopping keeps the clip loaded, and playing it again does not open it
        // afresh - so a turn made in between has to reach the film now.
        GalleryTile tile = TileOf(Clip);
        await PlayAsync(tile);
        _gallery.StopVideo();

        await _gallery.TurnPhotoCommand.ExecuteAsync("-90");
        _gallery.PlayVideoCommand.Execute(null);

        Assert.Equal(90d, _gallery.PlayingRotation);
        Assert.Equal(270, await RotationOnRecordAsync());
    }

    /// <summary>
    /// Opens the tile and plays it, the way the window does once the player says
    /// the clip is open.
    /// </summary>
    private async Task PlayAsync(GalleryTile tile)
    {
        _gallery.OpenTile = tile;
        _gallery.PlayVideoCommand.Execute(null);
        await _gallery.AlignPlaybackAsync(1280, 720, stillIsPortrait: false);
    }

    private async Task<int> RotationOnRecordAsync()
    {
        using IServiceScope scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GalleryDbContext>().Assets
            .AsNoTracking()
            .Where(asset => asset.Id == Clip)
            .Select(asset => asset.Rotation)
            .SingleAsync();
    }

    private (int Width, int Height) PosterSize()
    {
        using FileStream file = File.OpenRead(_thumbnails.ResolvePreviewPath(_poster));
        BitmapFrame frame = BitmapDecoder.Create(
            file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private GalleryTile TileOf(int id, int rotation = 0)
    {
        bool clip = id == Clip;
        string name = clip ? "clip.mp4" : "photo.jpg";
        return new GalleryTile(new GalleryItem(
            id,
            name,
            name,
            string.Empty,
            Path.Combine(_photos, name),
            clip ? _poster : null,
            null,
            new DateTime(2020, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            rotation,
            clip ? AssetKind.Video : AssetKind.Photo));
    }

    private void Seed()
    {
        File.WriteAllBytes(
            Path.Combine(_photos, "clip.mp4"),
            TestMp4.File(
                TestMp4.FileType(),
                TestMp4.Film(),
                TestMp4.Movie(TestMp4.SoundTrack(), TestMp4.VideoTrack(180, 1280, 720))));

        _poster = _thumbnails.SaveAsync(new GeneratedThumbnail(
            TestJpeg.Bytes(40, 30),
            TestJpeg.Bytes(80, 60),
            80,
            60,
            null,
            new PerceptualHash(1UL),
            1.ToString("x32"))).GetAwaiter().GetResult();

        using IServiceScope scope = _services.CreateScope();
        GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
        db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _photos });
        db.Assets.Add(new Asset
        {
            Id = Clip,
            PhotoSourceId = 1,
            RelativePath = "clip.mp4",
            Length = 1,
            ModifiedUtc = new DateTime(2020, 3, 1),
            CreatedUtc = new DateTime(2020, 3, 1),
            IndexedUtc = new DateTime(2020, 3, 1),
            Kind = AssetKind.Video,
            Status = AssetStatus.Ready,
            ThumbnailName = _poster,
            Width = 80,
            Height = 60,
        });
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
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Playing a clip where it is being looked at.
/// </summary>
/// <remarks>
/// [08] put playback out of scope on the grounds that it would mean bundling a
/// decoder. It does not: the poster already comes from whatever this machine can
/// decode, and the same codecs answer here. What has to be pinned instead is the
/// state around it - a clip that keeps playing under the next photograph, or a
/// play badge offered for a container that has just refused, are the two ways
/// this goes wrong quietly.
/// </remarks>
public sealed class VideoPlaybackTests
{
    [Fact]
    public void APhotographIsNeverOfferedAPlayBadge()
    {
        GalleryViewModel gallery = Showing(AssetKind.Photo);

        Assert.False(gallery.CanOfferPlay);
        Assert.False(gallery.IsPlayingVideo);
    }

    [Fact]
    public void AVideoIsOfferedOne()
    {
        Assert.True(Showing(AssetKind.Video).CanOfferPlay);
    }

    [Fact]
    public void OpeningAVideoWarmsThePlayerWithoutStartingIt()
    {
        // The header and first read over a share are seconds. They are spent
        // here, while the poster is on screen, instead of after somebody has
        // asked to watch.
        GalleryViewModel gallery = Showing(AssetKind.Video);

        Assert.Equal(_clip, gallery.PlayingPath);
        Assert.NotNull(gallery.PlayingSource);

        // Warmed is not playing. A video that starts because it was looked at
        // is the thing this must never become.
        Assert.False(gallery.IsPlayingVideo);
        Assert.True(gallery.CanOfferPlay);
    }

    [Fact]
    public void OpeningAPhotographWarmsNothing()
    {
        GalleryViewModel gallery = Showing(AssetKind.Photo);

        Assert.Null(gallery.PlayingPath);
    }

    [Fact]
    public void AVideoWhoseFileHasGoneIsNotWarmedAndSaysNothingYet()
    {
        // Silent on open: somebody who has not asked to watch anything should
        // not be told that they cannot.
        GalleryViewModel gallery = Showing(AssetKind.Video, exists: false);

        Assert.Null(gallery.PlayingPath);
        Assert.False(gallery.HasPlaybackError);
    }

    [Fact]
    public void PlayingTakesTheBadgeAwayAndNamesTheFile()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);

        gallery.PlayVideoCommand.Execute(null);

        Assert.True(gallery.IsPlayingVideo);
        Assert.Equal(_clip, gallery.PlayingPath);

        // The badge would sit over the clip it just started.
        Assert.False(gallery.CanOfferPlay);
    }

    [Fact]
    public void ThePathBecomesAUriThePlayerCanTake()
    {
        // The folder has a space in its name, as every folder in this library
        // does - "20250419 - Kidzania" - and a path that converted wrongly would
        // fail as a codec problem rather than as the path problem it is.
        GalleryViewModel gallery = Showing(AssetKind.Video);
        gallery.PlayVideoCommand.Execute(null);

        Assert.NotNull(gallery.PlayingSource);
        Assert.True(gallery.PlayingSource!.IsAbsoluteUri);
        Assert.Equal(_clip, gallery.PlayingSource.LocalPath);
    }

    [Fact]
    public void AClipWhoseFileHasGoneSaysItIsNotAvailable()
    {
        // The share is off, or the file was moved outside the app. The still is
        // this app's own copy and looks entirely present, so this is the moment
        // that has to be plain rather than a badge that does nothing.
        GalleryViewModel gallery = Showing(AssetKind.Video, exists: false);

        gallery.PlayVideoCommand.Execute(null);

        Assert.False(gallery.IsPlayingVideo);
        Assert.True(gallery.HasPlaybackError);
        Assert.Contains("not available", gallery.PlaybackError!, StringComparison.Ordinal);
    }

    [Fact]
    public void AClipWhoseFolderIsUnknownSaysSoToo()
    {
        // FullPath is empty exactly when the row's source has gone, which is the
        // one case where the app cannot even say where to look.
        GalleryViewModel gallery = Showing(AssetKind.Video, fullPath: string.Empty);

        gallery.PlayVideoCommand.Execute(null);

        Assert.False(gallery.IsPlayingVideo);
        Assert.Contains("not available", gallery.PlaybackError!, StringComparison.Ordinal);
    }

    [Fact]
    public void StoppingPutsTheStillBackButKeepsTheClipLoaded()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);
        gallery.PlayVideoCommand.Execute(null);

        gallery.StopVideo();

        Assert.False(gallery.IsPlayingVideo);
        Assert.True(gallery.CanOfferPlay);

        // The whole point: the source stays, so watching it again is a seek
        // rather than fetching the clip over the share a second time.
        Assert.NotNull(gallery.PlayingSource);
    }

    [Fact]
    public void MovingOnReleasesTheFileOnTheShare()
    {
        // The other side of keeping it loaded. Holding a file open on somebody's
        // network share while they browse their library is not a cache.
        GalleryViewModel gallery = Showing(AssetKind.Video);
        gallery.PlayVideoCommand.Execute(null);
        gallery.StopVideo();

        gallery.OpenTile = Tile(AssetKind.Photo);

        Assert.Null(gallery.PlayingSource);
        Assert.Null(gallery.PlayingPath);
    }

    [Fact]
    public void MovingToAnotherPictureStopsTheSound()
    {
        // The failure this exists for: a clip playing on under the photograph
        // that replaced it, with nothing on screen to stop it.
        GalleryViewModel gallery = Showing(AssetKind.Video);
        gallery.PlayVideoCommand.Execute(null);

        gallery.OpenTile = Tile(AssetKind.Photo);

        Assert.False(gallery.IsPlayingVideo);
        Assert.Null(gallery.PlayingPath);
    }

    [Fact]
    public void ClosingTheViewerStopsItToo()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);
        gallery.PlayVideoCommand.Execute(null);

        gallery.ClosePhoto();

        Assert.False(gallery.IsPlayingVideo);
    }

    [Fact]
    public void AContainerThatWillNotPlaySaysSoAndOffersSomethingElse()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);
        gallery.PlayVideoCommand.Execute(null);

        gallery.ReportPlaybackFailed();

        Assert.False(gallery.IsPlayingVideo);
        Assert.True(gallery.HasPlaybackError);
        Assert.Contains("cannot be played here", gallery.PlaybackError!, StringComparison.Ordinal);

        // No badge after a refusal: pressing it again gets the same nothing.
        Assert.False(gallery.CanOfferPlay);
    }

    [Fact]
    public void TheRefusalDoesNotFollowTheUserToTheNextClip()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);
        gallery.PlayVideoCommand.Execute(null);
        gallery.ReportPlaybackFailed();

        gallery.OpenTile = Tile(AssetKind.Video);

        Assert.False(gallery.HasPlaybackError);
        Assert.True(gallery.CanOfferPlay);
    }

    [Fact]
    public void APortraitClipInALandscapeStreamIsTurnedUpright()
    {
        // What a phone writes: the stream is landscape and a flag says which way
        // up it goes. The shell honours the flag when it makes the poster;
        // MediaElement does not, so the still is portrait and the film is not.
        GalleryViewModel gallery = Showing(AssetKind.Video);

        gallery.AlignPlaybackTo(streamIsPortrait: false, stillIsPortrait: true);

        Assert.Equal(90d, gallery.PlayingRotation);
    }

    [Fact]
    public void AClipAlreadyTheRightWayUpIsLeftAlone()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);

        gallery.AlignPlaybackTo(streamIsPortrait: false, stillIsPortrait: false);

        Assert.Equal(0d, gallery.PlayingRotation);
    }

    [Fact]
    public void APortraitStreamWithAPortraitStillIsLeftAlone()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);

        gallery.AlignPlaybackTo(streamIsPortrait: true, stillIsPortrait: true);

        Assert.Equal(0d, gallery.PlayingRotation);
    }

    [Fact]
    public void TurningWrapsRatherThanRunningAway()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);

        gallery.TurnPlayingVideo(90);
        gallery.TurnPlayingVideo(90);
        gallery.TurnPlayingVideo(90);
        gallery.TurnPlayingVideo(90);

        Assert.Equal(0d, gallery.PlayingRotation);
    }

    [Fact]
    public void TurningBackFromUprightGoesTheLongWayRoundRatherThanNegative()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);

        gallery.TurnPlayingVideo(-90);

        Assert.Equal(270d, gallery.PlayingRotation);
    }

    [Fact]
    public void TheTurnDoesNotFollowTheUserToTheNextPicture()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);
        gallery.PlayVideoCommand.Execute(null);
        gallery.TurnPlayingVideo(90);

        gallery.OpenTile = Tile(AssetKind.Video);

        Assert.Equal(0d, gallery.PlayingRotation);
    }

    /// <summary>
    /// A real file in a folder whose name has a space, because the check that
    /// the clip is still there is now part of what is being tested.
    /// </summary>
    private readonly string _clip = CreateClip();

    private static string CreateClip()
    {
        string folder = Path.Combine(
            Path.GetTempPath(), $"pg-play-{Guid.NewGuid():N}", "20250419 - Kidzania");
        Directory.CreateDirectory(folder);

        string clip = Path.Combine(folder, "clip.MOV");
        File.WriteAllBytes(clip, [0]);
        return clip;
    }

    /// <summary>
    /// A view model with nothing behind it, which is all these need.
    /// </summary>
    /// <remarks>
    /// Opening a picture asks for its details in the background and gives up
    /// quietly when the service is not registered, so an empty container is a
    /// working stand-in - and none of this touches the thumbnail store, because
    /// the tests set the open tile rather than going through the command that
    /// draws it.
    /// </remarks>
    private GalleryViewModel Showing(
        AssetKind kind, bool exists = true, string? fullPath = null)
    {
        ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var workingFolder = new WorkingFolder(
            Path.Combine(Path.GetTempPath(), $"pg-play-{Guid.NewGuid():N}"));

        var gallery = new GalleryViewModel(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));

        gallery.OpenTile = Tile(
            kind,
            fullPath ?? (exists ? _clip : Path.Combine(Path.GetTempPath(), "gone", "clip.MOV")));

        return gallery;
    }

    private GalleryTile Tile(AssetKind kind) => Tile(kind, _clip);

    private static GalleryTile Tile(AssetKind kind, string fullPath) =>
        new(new GalleryItem(
            1,
            @"20250419 - Kidzania\clip.MOV",
            "clip.MOV",
            "20250419 - Kidzania",
            fullPath,
            "ab/abcdef.jpg",
            null,
            new DateTime(2025, 4, 19, 0, 0, 0, DateTimeKind.Utc),
            0,
            kind));
}

using System.Diagnostics;
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
    public async Task OpeningAVideoWarmsThePlayerWithoutStartingIt()
    {
        // The header and first read over a share are seconds. They are spent
        // here, while the poster is on screen, instead of after somebody has
        // asked to watch.
        GalleryViewModel gallery = Showing(AssetKind.Video);
        await gallery.Warming;

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
    public async Task AVideoWhoseFileHasGoneIsNotWarmedAndSaysNothingYet()
    {
        // Silent on open: somebody who has not asked to watch anything should
        // not be told that they cannot.
        GalleryViewModel gallery = Showing(AssetKind.Video, exists: false);
        await gallery.Warming;

        Assert.Null(gallery.PlayingPath);
        Assert.False(gallery.HasPlaybackError);
    }

    [Fact]
    public void SteppingOntoAVideoOnAShareThatDoesNotAnswerDoesNotWait()
    {
        // The share switched off: Windows takes 21 seconds to say a file there
        // does not exist, and asked on the window's thread that froze the
        // viewer on every video stepped onto. 192.0.2.1 is reserved for
        // documentation and never answers.
        var stepping = Stopwatch.StartNew();

        GalleryViewModel gallery = Showing(AssetKind.Video, fullPath: UnansweredClip);

        Assert.True(
            stepping.Elapsed < TimeSpan.FromSeconds(2),
            $"opening the video waited {stepping.Elapsed.TotalSeconds:N1} s for the share");
        Assert.Null(gallery.PlayingPath);
    }

    [Fact]
    public void PressingPlayOnAShareThatDoesNotAnswerDoesNotWaitEither()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video, fullPath: UnansweredClip);
        var pressing = Stopwatch.StartNew();

        gallery.PlayVideoCommand.Execute(null);

        Assert.True(
            pressing.Elapsed < TimeSpan.FromSeconds(2),
            $"pressing play waited {pressing.Elapsed.TotalSeconds:N1} s for the share");
        Assert.False(gallery.IsPlayingVideo);
    }

    [Fact]
    public async Task AClipLeftBeforeTheShareAnswersIsNotHandedToThePlayer()
    {
        // The answer arrives for a picture the viewer has already left, and a
        // late answer must not load the old clip under the new photograph.
        GalleryViewModel gallery = Showing(AssetKind.Video);
        Task warming = gallery.Warming;

        gallery.OpenTile = Tile(AssetKind.Photo);
        await warming;

        Assert.Null(gallery.PlayingPath);
    }

    [Fact]
    public async Task PlayingAClipThatWasNotWarmedFindsItThen()
    {
        // The file was not there when the picture opened - the share was still
        // waking - and is by the time somebody presses play.
        string later = Path.Combine(Path.GetDirectoryName(_clip)!, "later.MOV");
        GalleryViewModel gallery = Showing(AssetKind.Video, fullPath: later);
        await gallery.Warming;
        File.WriteAllBytes(later, [0]);

        await gallery.PlayVideoCommand.ExecuteAsync(null);

        Assert.True(gallery.IsPlayingVideo);
        Assert.Equal(later, gallery.PlayingPath);
    }

    private const string UnansweredClip = @"\\192.0.2.1\PhotoGallery\clip.MOV";

    [Fact]
    public async Task PlayingTakesTheBadgeAwayAndNamesTheFile()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);

        await gallery.PlayVideoCommand.ExecuteAsync(null);

        Assert.True(gallery.IsPlayingVideo);
        Assert.Equal(_clip, gallery.PlayingPath);

        // The badge would sit over the clip it just started.
        Assert.False(gallery.CanOfferPlay);
    }

    [Fact]
    public async Task ThePathBecomesAUriThePlayerCanTake()
    {
        // The folder has a space in its name, as every folder in this library
        // does - "20210710 - Zoo Visit" - and a path that converted wrongly would
        // fail as a codec problem rather than as the path problem it is.
        GalleryViewModel gallery = Showing(AssetKind.Video);
        await gallery.PlayVideoCommand.ExecuteAsync(null);

        Assert.NotNull(gallery.PlayingSource);
        Assert.True(gallery.PlayingSource!.IsAbsoluteUri);
        Assert.Equal(_clip, gallery.PlayingSource.LocalPath);
    }

    [Fact]
    public async Task AClipWhoseFileHasGoneSaysItIsNotAvailable()
    {
        // The share is off, or the file was moved outside the app. The still is
        // this app's own copy and looks entirely present, so this is the moment
        // that has to be plain rather than a badge that does nothing.
        GalleryViewModel gallery = Showing(AssetKind.Video, exists: false);

        await gallery.PlayVideoCommand.ExecuteAsync(null);

        Assert.False(gallery.IsPlayingVideo);
        Assert.True(gallery.HasPlaybackError);
        Assert.Contains("not available", gallery.PlaybackError!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClipWhoseFolderIsUnknownSaysSoToo()
    {
        // FullPath is empty exactly when the row's source has gone, which is the
        // one case where the app cannot even say where to look.
        GalleryViewModel gallery = Showing(AssetKind.Video, fullPath: string.Empty);

        await gallery.PlayVideoCommand.ExecuteAsync(null);

        Assert.False(gallery.IsPlayingVideo);
        Assert.Contains("not available", gallery.PlaybackError!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoppingPutsTheStillBackButKeepsTheClipLoaded()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);
        await gallery.PlayVideoCommand.ExecuteAsync(null);

        gallery.StopVideo();

        Assert.False(gallery.IsPlayingVideo);
        Assert.True(gallery.CanOfferPlay);

        // The whole point: the source stays, so watching it again is a seek
        // rather than fetching the clip over the share a second time.
        Assert.NotNull(gallery.PlayingSource);
    }

    [Fact]
    public async Task MovingOnReleasesTheFileOnTheShare()
    {
        // The other side of keeping it loaded. Holding a file open on somebody's
        // network share while they browse their library is not a cache.
        GalleryViewModel gallery = Showing(AssetKind.Video);
        await gallery.PlayVideoCommand.ExecuteAsync(null);
        gallery.StopVideo();

        gallery.OpenTile = Tile(AssetKind.Photo);

        Assert.Null(gallery.PlayingSource);
        Assert.Null(gallery.PlayingPath);
    }

    [Fact]
    public async Task MovingToAnotherPictureStopsTheSound()
    {
        // The failure this exists for: a clip playing on under the photograph
        // that replaced it, with nothing on screen to stop it.
        GalleryViewModel gallery = Showing(AssetKind.Video);
        await gallery.PlayVideoCommand.ExecuteAsync(null);

        gallery.OpenTile = Tile(AssetKind.Photo);

        Assert.False(gallery.IsPlayingVideo);
        Assert.Null(gallery.PlayingPath);
    }

    [Fact]
    public async Task ClosingTheViewerStopsItToo()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);
        await gallery.PlayVideoCommand.ExecuteAsync(null);

        gallery.ClosePhoto();

        Assert.False(gallery.IsPlayingVideo);
    }

    [Fact]
    public async Task AContainerThatWillNotPlaySaysSoAndOffersSomethingElse()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);
        await gallery.PlayVideoCommand.ExecuteAsync(null);

        gallery.ReportPlaybackFailed();

        Assert.False(gallery.IsPlayingVideo);
        Assert.True(gallery.HasPlaybackError);
        Assert.Contains("cannot be played here", gallery.PlaybackError!, StringComparison.Ordinal);

        // No badge after a refusal: pressing it again gets the same nothing.
        Assert.False(gallery.CanOfferPlay);
    }

    [Fact]
    public async Task TheRefusalDoesNotFollowTheUserToTheNextClip()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);
        await gallery.PlayVideoCommand.ExecuteAsync(null);
        gallery.ReportPlaybackFailed();

        gallery.OpenTile = Tile(AssetKind.Video);

        Assert.False(gallery.HasPlaybackError);
        Assert.True(gallery.CanOfferPlay);
    }

    [Fact]
    public async Task AClipIsDrawnTheWayItsFileSays()
    {
        // Filed upside down: stream and poster both landscape, which the shape
        // guess read as already right.
        GalleryViewModel gallery = Showing(
            AssetKind.Video, reader: new FixedReader(new VideoOrientation(180, 1280, 720)));

        await gallery.Warming;
        await gallery.AlignPlaybackAsync(1280, 720, stillIsPortrait: false);

        Assert.Equal(180d, gallery.PlayingRotation);
    }

    [Fact]
    public async Task TheSavedTurnIsAddedToTheFiles()
    {
        GalleryViewModel gallery = Showing(
            AssetKind.Video,
            reader: new FixedReader(new VideoOrientation(180, 1280, 720)),
            rotation: 90);

        await gallery.Warming;
        await gallery.AlignPlaybackAsync(1280, 720, stillIsPortrait: true);

        Assert.Equal(270d, gallery.PlayingRotation);
    }

    [Fact]
    public async Task AStreamThePlayerHasTurnedItselfIsNotTurnedAgain()
    {
        GalleryViewModel gallery = Showing(
            AssetKind.Video, reader: new FixedReader(new VideoOrientation(90, 1920, 1080)));

        await gallery.Warming;
        await gallery.AlignPlaybackAsync(1080, 1920, stillIsPortrait: true);

        Assert.Equal(0d, gallery.PlayingRotation);
    }

    [Fact]
    public async Task WithoutANoteAPortraitClipInALandscapeStreamIsTurnedUpright()
    {
        // A file that cannot say - here, a one-byte clip with no reader behind
        // it - falls back to the shape: the poster is upright, so a portrait
        // poster over a landscape stream needs a quarter turn.
        GalleryViewModel gallery = Showing(AssetKind.Video);

        await gallery.Warming;
        await gallery.AlignPlaybackAsync(1920, 1080, stillIsPortrait: true);

        Assert.Equal(90d, gallery.PlayingRotation);
    }

    [Fact]
    public async Task WithoutANoteAClipAlreadyTheRightWayUpIsLeftAlone()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);

        await gallery.Warming;
        await gallery.AlignPlaybackAsync(1920, 1080, stillIsPortrait: false);

        Assert.Equal(0d, gallery.PlayingRotation);
    }

    [Fact]
    public async Task WithoutANoteAPortraitStreamWithAPortraitStillIsLeftAlone()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);

        await gallery.Warming;
        await gallery.AlignPlaybackAsync(1080, 1920, stillIsPortrait: true);

        Assert.Equal(0d, gallery.PlayingRotation);
    }

    [Fact]
    public async Task AnAnswerForAClipLeftBehindIsNotApplied()
    {
        // Held until the viewer has moved on, so the answer always arrives late.
        // Answered at once, it could land on another thread at the very moment
        // the test switched pictures - which the app's single window thread
        // never allows, and which made this test fail one run in a few.
        using var held = new HeldReader(new VideoOrientation(180, 1280, 720));
        GalleryViewModel gallery = Showing(AssetKind.Video, reader: held);
        await gallery.Warming;
        Task aligning = gallery.AlignPlaybackAsync(1280, 720, stillIsPortrait: false);

        gallery.OpenTile = Tile(AssetKind.Photo);
        held.Answer();
        await aligning;

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
    public async Task TheTurnDoesNotFollowTheUserToTheNextPicture()
    {
        GalleryViewModel gallery = Showing(AssetKind.Video);
        await gallery.PlayVideoCommand.ExecuteAsync(null);
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
            Path.GetTempPath(), $"pg-play-{Guid.NewGuid():N}", "20210710 - Zoo Visit");
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
        AssetKind kind,
        bool exists = true,
        string? fullPath = null,
        IVideoOrientationReader? reader = null,
        int rotation = 0)
    {
        var registered = new ServiceCollection();
        if (reader is not null)
        {
            registered.AddSingleton(reader);
        }

        ServiceProvider services = registered.BuildServiceProvider();
        var workingFolder = new WorkingFolder(
            Path.Combine(Path.GetTempPath(), $"pg-play-{Guid.NewGuid():N}"));

        var gallery = new GalleryViewModel(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));

        gallery.OpenTile = Tile(
            kind,
            fullPath ?? (exists ? _clip : Path.Combine(Path.GetTempPath(), "gone", "clip.MOV")),
            rotation);

        return gallery;
    }

    private GalleryTile Tile(AssetKind kind) => Tile(kind, _clip);

    private static GalleryTile Tile(AssetKind kind, string fullPath, int rotation = 0) =>
        new(new GalleryItem(
            1,
            @"20210710 - Zoo Visit\clip.MOV",
            "clip.MOV",
            "20210710 - Zoo Visit",
            fullPath,
            "ab/abcdef.jpg",
            null,
            new DateTime(2021, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            rotation,
            kind));

    /// <summary>A file that says the same thing whatever is asked.</summary>
    private sealed class FixedReader(VideoOrientation? says) : IVideoOrientationReader
    {
        public VideoOrientation? Read(string fullPath) => says;
    }

    /// <summary>A reader that does not answer until the test says so.</summary>
    private sealed class HeldReader(VideoOrientation? says) : IVideoOrientationReader, IDisposable
    {
        private readonly ManualResetEventSlim _answer = new();

        public void Answer() => _answer.Set();

        public VideoOrientation? Read(string fullPath)
        {
            _answer.Wait(TimeSpan.FromSeconds(10));
            return says;
        }

        public void Dispose()
        {
            _answer.Set();
            _answer.Dispose();
        }
    }
}

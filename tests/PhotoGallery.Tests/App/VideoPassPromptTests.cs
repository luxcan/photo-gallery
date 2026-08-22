using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.OpenLibrary;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Telling somebody that their videos are waiting for a second pass.
/// </summary>
/// <remarks>
/// Scanning indexes videos and prepares photographs. Making a picture out of a
/// clip is a separate, much more expensive pass with its own button - and until
/// this, nothing said so: the scan reported what it had done to the pictures,
/// the videos it had just brought in stayed out of the grid, and the user was
/// left to work out that a button they had not pressed was the reason.
/// </remarks>
public sealed class VideoPassPromptTests : IDisposable
{
    [Fact]
    public void VideosWithNoPictureAreCountedAndTheButtonIsNamed()
    {
        Open(videos: 4_743, prepared: 1_183);

        Assert.Contains("3,560 of 4,743", _main.VideoStatus, StringComparison.Ordinal);

        // The number on its own is a fact about the library. What makes it
        // actionable is saying what turns it into zero - which is scanning
        // again, now that there is no second button to point at.
        Assert.Contains("Scanning carries on", _main.VideoStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void ItSaysWhyTheyAreNotInTheLibrary()
    {
        // The confusing part is not the count - it is opening Library, finding
        // photographs only, and having no reason for it.
        Open(videos: 10, prepared: 0);

        Assert.Contains("not in your library", _main.VideoStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void AFinishedLibraryIsNotNagged()
    {
        Open(videos: 4_743, prepared: 4_743);

        Assert.Equal("All 4,743 videos have a picture.", _main.VideoStatus);
    }

    [Fact]
    public void ClipsThatWillNotOpenAreNotCountedAsWaiting()
    {
        // The bug this exists for: a clip that will not decode keeps a null
        // rendition name for ever and is never offered to the pass again, so
        // counting it as waiting had the screen promising a rescan that was
        // never coming - and made the sentence above unreachable on any real
        // library, because one bad clip in four thousand is normal.
        Open(videos: 4_743, prepared: 4_737, unreadable: 6);

        Assert.DoesNotContain("Scanning carries on", _main.VideoStatus, StringComparison.Ordinal);
        Assert.Equal(
            "All 4,737 videos have a picture. The other 6 will not open on this computer.",
            _main.VideoStatus);
    }

    [Fact]
    public void WhatIsStillWaitingExcludesWhatWillNeverOpen()
    {
        Open(videos: 100, prepared: 40, unreadable: 10);

        // Fifty, not sixty: the ten are settled, however permanently.
        Assert.Contains("50 of 100", _main.VideoStatus, StringComparison.Ordinal);
        Assert.Contains("Scanning carries on", _main.VideoStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void ALibraryOfPhotographsIsToldNothingAboutVideos()
    {
        // No videos at all: a line about a pass that has nothing to do is noise
        // on a screen that already carries the folders and the scan.
        Open(videos: 0, prepared: 0);

        Assert.Equal(string.Empty, _main.VideoStatus);
    }

    [Fact]
    public void WaitingIsWhatIsIndexedMinusWhatHasAPicture()
    {
        var counts = new LibraryCounts(
            Photos: 11_085,
            Videos: 4_743,
            VideosPrepared: 1_183,
            VideosUnreadable: 6,
            Thumbnails: 12_268,
            Faces: 16_857,
            People: 42,
            UnresolvedDuplicateSets: 3);

        // Not 3,560: the six that will not decode are never offered again, so
        // they are not waiting for anything.
        Assert.Equal(3_554, counts.VideosWaiting);
        Assert.Equal(15_828, counts.TotalAssets);
    }

    private void Open(int videos, int prepared, int unreadable = 0) =>
        _main.ApplyOpenResult(new OpenLibraryResult(
            _root,
            [],
            new Dictionary<int, int>(),
            new LibraryCounts(
                Photos: 0,
                Videos: videos,
                VideosPrepared: prepared,
                VideosUnreadable: unreadable,
                Thumbnails: prepared,
                Faces: 0,
                People: 0,
                UnresolvedDuplicateSets: 0),
            ThemePreference.System,
            GalleryCellSize: 220d,
            GallerySortOrder.NewestFirst,
            NavigationCollapsed: false,
            WasCreated: false));

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    public VideoPassPromptTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-videoprompt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        // Nothing here resolves a service: opening applies a result that has
        // already been read, which is exactly the seam being tested.
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

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A working folder left in the temp directory is not a failure.
        }
    }
}

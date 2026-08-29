using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.Models;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.Sharing;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Models;
using PhotoGallery.Application.UseCases.OpenLibrary;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What the app says once a model is installed and has not been applied yet.
/// </summary>
/// <remarks>
/// The state every first install lands in, and the one this app was worst at.
/// The models are downloaded after the library has been scanned; both passes
/// that use them run as part of a scan; so the features unlock and nothing
/// happens. Nothing was broken, nothing said so, and the way out - scan again -
/// was something the user had to already know.
/// </remarks>
public sealed class ScanToApplyTests : IDisposable
{
    [Fact]
    public void AnInstalledModelWithWorkOutstandingOffersTheScan()
    {
        // Both installed, so nothing is waiting on a download and the scan is
        // the only thing left between the user and the feature.
        Install(ModelFeature.Faces, ModelFeature.ContentSearch);
        Open(awaitingFaces: 595);

        Assert.True(_main.NeedsFaceScan);
        Assert.True(_main.CanScanToApply);
        Assert.Contains(
            "Scan your folders", _main.SearchNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void ALibraryThatHasBeenLookedAtIsNotNagged()
    {
        // A library of landscapes: looked at, and no faces in it. Counting faces
        // rather than outstanding work would offer this scan for ever.
        Install(ModelFeature.Faces, ModelFeature.ContentSearch);
        Open(awaitingFaces: 0);

        Assert.False(_main.NeedsFaceScan);
        Assert.False(_main.CanScanToApply);
        Assert.Equal(string.Empty, _main.SearchNotice);
    }

    [Fact]
    public void WhatIsNotInstalledIsNotSomethingAScanCanApply()
    {
        // Nothing installed and everything outstanding. Telling somebody to scan
        // for a feature they have not downloaded is an instruction that cannot
        // work, so the notice stays the download one.
        Open(awaitingFaces: 595, awaitingDescription: 595);

        Assert.False(_main.NeedsFaceScan);
        Assert.False(_main.CanScanToApply);
        Assert.Contains("Install", _main.SearchNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void OneModelInstalledAndOneNotSaysTheDownloadFirst()
    {
        // Both are true at once, and only one of them can be acted on: the
        // missing model cannot be applied by scanning.
        Install(ModelFeature.Faces);
        Open(awaitingFaces: 12, awaitingDescription: 12);

        Assert.True(_main.NeedsFaceScan);
        Assert.False(_main.CanScanToApply);
        Assert.Contains(
            "picture-description model", _main.SearchNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void BothInstalledAndNeitherAppliedIsOneSentence()
    {
        Install(ModelFeature.Faces, ModelFeature.ContentSearch);
        Open(awaitingFaces: 595, awaitingDescription: 595);

        Assert.Equal(
            "The models are installed. Scan your folders to find the faces in your "
            + "pictures and read what they are of.",
            _main.SearchNotice);
    }

    [Fact]
    public void CheckEveryoneIsOffUntilThereAreFacesToCheck()
    {
        // The button somebody presses when their People screen is empty, because
        // it describes what they want. Before a scan it can only answer "nobody
        // has been named yet", which reads as a refusal - so it says why on
        // itself instead, and the scan is the only thing left to press.
        Install(ModelFeature.Faces, ModelFeature.ContentSearch);
        Open(awaitingFaces: 595, faces: 0);

        Assert.False(_main.RecheckPeopleCommand.CanExecute(null));
        Assert.Contains("Find faces now", _main.RecheckHint, StringComparison.Ordinal);
    }

    [Fact]
    public void OnceThereAreFacesItIsTheRightButtonAgain()
    {
        Install(ModelFeature.Faces, ModelFeature.ContentSearch);
        Open(awaitingFaces: 0, faces: 962);

        Assert.True(_main.RecheckPeopleCommand.CanExecute(null));
        Assert.Contains(
            "every picture again", _main.RecheckHint, StringComparison.Ordinal);
    }

    /// <summary>Pretends those features' files are all present and proved.</summary>
    private void Install(params ModelFeature[] features) =>
        _main.Models.Features =
        [
            .. features.Select(feature => FeatureCard.Of(new FeatureStatus(
                feature,
                [
                    .. FeatureModels.Of(feature).Select(id => new ModelFileStatus(
                        id, $"{id}.onnx", 1024, "Test terms.", ModelState.Ready)),
                ]))),
        ];

    private void Open(int awaitingFaces = 0, int awaitingDescription = 0, int faces = 0) =>
        _main.ApplyOpenResult(new OpenLibraryResult(
            _root,
            [],
            new Dictionary<int, int>(),
            new LibraryCounts(
                Photos: 595,
                Videos: 0,
                VideosPrepared: 0,
                VideosUnreadable: 0,
                Thumbnails: 595,
                Faces: faces,
                People: 0,
                UnresolvedDuplicateSets: 0,
                AwaitingFaces: awaitingFaces,
                AwaitingDescription: awaitingDescription),
            ThemePreference.System,
            GalleryCellSize: 220d,
            GallerySortOrder.NewestFirst,
            NavigationCollapsed: false,
            WasCreated: false));

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    public ScanToApplyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-scanapply-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

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

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is not a failed test.
        }
    }
}

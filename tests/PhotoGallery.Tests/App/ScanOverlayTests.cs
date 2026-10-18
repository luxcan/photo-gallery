using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.UseCases.Refresh;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What the overlay says while a scan runs, one report at a time.
/// </summary>
/// <remarks>
/// The overlay has two lines of words: the phase's name, and a line under it
/// saying what it is working on or what it is for. Every phase but one wrote
/// both, and the one that did not - preparing pictures - kept whatever the phase
/// before it had left. That was the crawl's folder until the scan learned to
/// take from the sync folder first; then the family library's scan read
/// "Preparing pictures / Looking in the sync folder" for the whole picture
/// phase, which is a phase that never goes near the sync folder.
/// </remarks>
public sealed class ScanOverlayTests : IDisposable
{
    /// <summary>A line the phase before left behind, which no phase may keep.</summary>
    private const string LeftBehind = "left behind by the phase before";

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    public ScanOverlayTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-scanoverlay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        // Nothing is resolved: these tests hand the overlay its reports directly,
        // which is all a scan does to it.
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

    public static TheoryData<RefreshPhase> EveryPhase => [.. Enum.GetValues<RefreshPhase>()];

    /// <summary>
    /// Preparing pictures says its own line, not the one the take left behind.
    /// </summary>
    [Theory]
    [InlineData("Looking in the sync folder")]
    [InlineData("Pictures made on your other computers")]
    [InlineData("Faces found on your other computers")]
    public void PreparingPicturesDoesNotKeepTheLineTheTakeLeft(string taking)
    {
        _main.ShowRefreshProgress(new RefreshProgress(RefreshPhase.TakingCopies, taking, 0, 4, 0));
        Assert.Equal(taking, _main.OverlayTarget);

        _main.ShowRefreshProgress(new RefreshProgress(RefreshPhase.Generating, string.Empty, 3, 20, 0));

        Assert.Equal("Preparing pictures", _main.OverlayTitle);
        Assert.Equal("from the originals in your folders", _main.OverlayTarget);
    }

    /// <summary>
    /// Every phase a scan reports sets or clears the line under its name.
    /// </summary>
    /// <remarks>
    /// Over every phase there is, so that the next phase somebody adds cannot
    /// inherit a line by forgetting to write one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPhase))]
    public void EveryPhaseWritesTheLineUnderItsOwnName(RefreshPhase phase)
    {
        _main.OverlayTarget = LeftBehind;

        _main.ShowRefreshProgress(new RefreshProgress(phase, string.Empty, 1, 10, 0));

        Assert.NotEqual(LeftBehind, _main.OverlayTarget);
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

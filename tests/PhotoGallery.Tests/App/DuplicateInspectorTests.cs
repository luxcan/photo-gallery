using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Deciding a group from the whole picture rather than from the thumbnails.
/// </summary>
/// <remarks>
/// The toggle over the picture and the checkbox under the card are one fact.
/// They were not: the button wrote the app's suggested keeper, which nothing on
/// this screen acts on, and then reloaded - so it appeared to do nothing while
/// clearing every tick the user had made. These pin both halves down.
/// </remarks>
public sealed class DuplicateInspectorTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly DuplicatesViewModel _duplicates;

    public DuplicateInspectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-dupe-inspect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        // Deliberately empty: nothing the inspector does may reach a service.
        // If it ever does again, ReloadAsync swallows the failure into Status -
        // which is what several of these assert on.
        _services = new ServiceCollection().BuildServiceProvider();
        _duplicates = new DuplicatesViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    [Fact]
    public async Task Toggling_TicksTheSameCopyTheCheckboxWould()
    {
        DuplicateSetItem set = Showing(DuplicateKind.Near, copies: 3);
        await InspectAsync(set.Copies[1]);

        _duplicates.IsInspectedKept = true;

        Assert.True(set.Copies[1].IsKept);
        Assert.Single(set.Kept);
        Assert.True(set.CanDelete);
    }

    [Fact]
    public async Task Toggling_NeitherClosesThePictureNorReloadsTheScreen()
    {
        // The whole of the old bug. Reloading rebuilt every group from the
        // index, so one press cleared the ticks in every other group too.
        DuplicateSetItem set = Showing(DuplicateKind.Near, copies: 2);
        await InspectAsync(set.Copies[0]);

        _duplicates.IsInspectedKept = true;

        Assert.True(_duplicates.IsInspecting);
        Assert.Equal(string.Empty, _duplicates.Status);
    }

    [Fact]
    public async Task Toggling_SaysWhichWayThisCopyCurrentlySits()
    {
        DuplicateSetItem set = Showing(DuplicateKind.Near, copies: 3);
        await InspectAsync(set.Copies[0]);

        // Nothing ticked anywhere in the group: this copy is not going to be
        // deleted, and saying so would be a threat the screen will not carry out.
        Assert.Equal("Not chosen yet", _duplicates.InspectedDecision);

        _duplicates.IsInspectedKept = true;
        Assert.Equal("Keeping this one", _duplicates.InspectedDecision);

        _duplicates.IsInspectedKept = false;
        Assert.Equal("Not chosen yet", _duplicates.InspectedDecision);
    }

    [Fact]
    public async Task Stepping_ToAnUntickedCopyOfADecidedGroupSaysItIsGoing()
    {
        DuplicateSetItem set = Showing(DuplicateKind.Near, copies: 3);
        await InspectAsync(set.Copies[0]);
        _duplicates.IsInspectedKept = true;

        await _duplicates.InspectNextCommand.ExecuteAsync(null);

        Assert.Same(set.Copies[1], _duplicates.Inspected);
        Assert.False(_duplicates.IsInspectedKept);
        Assert.Equal("Deleting this one", _duplicates.InspectedDecision);
    }

    [Fact]
    public async Task Keeping_SeveralIsAllowedFromThePictureWhereItIsAllowedAtAll()
    {
        // The reason the button could not stay a single "keep this one": a burst
        // of shots seconds apart lands in one visually-alike group and several of
        // them can be photographs worth having.
        DuplicateSetItem set = Showing(DuplicateKind.Near, copies: 3);

        await InspectAsync(set.Copies[0]);
        _duplicates.IsInspectedKept = true;
        await InspectAsync(set.Copies[1]);
        _duplicates.IsInspectedKept = true;

        Assert.Equal(2, set.Kept.Count);
        Assert.Single(set.Doomed);
    }

    [Fact]
    public async Task Keeping_AnIdenticalCopyStillUnticksTheRestOfItsGroup()
    {
        // Same bytes, so a second one is storage spent on nothing. The group
        // enforces that, and reaching the tick from the picture must not dodge it.
        DuplicateSetItem set = Showing(DuplicateKind.Exact, copies: 3);

        await InspectAsync(set.Copies[0]);
        _duplicates.IsInspectedKept = true;
        await InspectAsync(set.Copies[2]);
        _duplicates.IsInspectedKept = true;

        Assert.Equal([false, false, true], set.Copies.Select(copy => copy.IsKept));
        Assert.Equal("Keeping this one", _duplicates.InspectedDecision);
    }

    [Fact]
    public async Task Closing_ThePictureLeavesTheTickBehind()
    {
        DuplicateSetItem set = Showing(DuplicateKind.Near, copies: 2);
        await InspectAsync(set.Copies[0]);
        _duplicates.IsInspectedKept = true;

        _duplicates.CloseInspectCommand.Execute(null);

        Assert.True(set.Copies[0].IsKept);
        Assert.False(_duplicates.IsInspecting);
        Assert.Equal(string.Empty, _duplicates.InspectedDecision);
    }

    private Task InspectAsync(DuplicateCopyItem copy) =>
        _duplicates.InspectCopyCommand.ExecuteAsync(copy);

    /// <summary>One group, on the tab that is showing.</summary>
    private DuplicateSetItem Showing(DuplicateKind kind, int copies)
    {
        var set = new DuplicateSetItem(new DuplicateSetView(
            1,
            kind,
            [
                .. Enumerable.Range(0, copies).Select(i => new DuplicateCopy(
                    AssetId: i + 1,
                    PhotoSourceId: 1,
                    RelativePath: $@"20230201\photo-{i}.jpg",
                    FullPath: $@"C:\pictures\20230201\photo-{i}.jpg",

                    // No rendition, so opening one decodes nothing and the test
                    // needs no picture on disk.
                    ThumbnailName: null,
                    Length: 1000,
                    Width: 100,
                    Height: 100,
                    Role: i == 0 ? DuplicateRole.Keeper : DuplicateRole.Redundant,
                    Distance: i)),
            ]));

        _duplicates.ShowNear = kind == DuplicateKind.Near;
        (_duplicates.ShowNear ? _duplicates.Near : _duplicates.Exact).Add(set);
        return set;
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

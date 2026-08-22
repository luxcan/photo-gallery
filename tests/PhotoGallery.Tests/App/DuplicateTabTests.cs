using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The two lists on the Duplicates screen, and everything that reads whichever
/// one is showing.
/// </summary>
/// <remarks>
/// Identical and visually-alike are deliberately separate lists behind one set
/// of controls, which means every total and every button below them has to move
/// when the tab does. Missing that left the button acting on the whole screen
/// reporting on the list you were no longer looking at.
/// </remarks>
public sealed class DuplicateTabTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly DuplicatesViewModel _duplicates;

    public DuplicateTabTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-dupe-tabs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        // Nothing here resolves a service: what is under test is which list the
        // screen is reading, not what it would load.
        _services = new ServiceCollection().BuildServiceProvider();
        _duplicates = new DuplicatesViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    [Fact]
    public void SwitchingTab_MovesTheDeleteButtonOnToTheOtherList()
    {
        // Ticking a copy under "Looks the same" used to leave the button
        // disabled, because it was still answering for the identical list.
        DuplicateSetItem near = SetOf(1);
        near.Copies[0].IsKept = true;
        _duplicates.Near.Add(near);

        Assert.False(_duplicates.CanDeleteChosen);

        _duplicates.ShowNear = true;

        Assert.True(_duplicates.CanDeleteChosen);
        Assert.Single(_duplicates.Chosen);
    }

    [Fact]
    public void SwitchingTab_DoesNotCarryTheOtherListsTotalsAcross()
    {
        // The other way round, and worse: a button that looked ready and did
        // nothing, because the count came from a list that was no longer shown.
        DuplicateSetItem exact = SetOf(1);
        exact.Copies[0].IsKept = true;
        _duplicates.Exact.Add(exact);
        _duplicates.Near.Add(SetOf(2));

        _duplicates.ShowNear = true;

        Assert.False(_duplicates.CanDeleteChosen);
        Assert.Empty(_duplicates.Chosen);
    }

    [Fact]
    public void SwitchingTab_KeepsWhatWasTickedOnEachList()
    {
        // The lists are separate decisions. Coming back to one should find it
        // as it was left.
        DuplicateSetItem exact = SetOf(1);
        exact.Copies[0].IsKept = true;
        _duplicates.Exact.Add(exact);

        _duplicates.ShowNear = true;
        _duplicates.ShowNear = false;

        Assert.True(_duplicates.CanDeleteChosen);
        Assert.True(exact.Copies[0].IsKept);
    }

    private static DuplicateSetItem SetOf(int id) =>
        new(new DuplicateSetView(
            id,
            DuplicateKind.Near,
            [
                .. Enumerable.Range(0, 2).Select(i => new DuplicateCopy(
                    AssetId: (id * 10) + i,
                    PhotoSourceId: 1,
                    RelativePath: $@"20230201\photo-{id}-{i}.jpg",
                    FullPath: $@"C:\pictures\20230201\photo-{id}-{i}.jpg",
                    ThumbnailName: null,
                    Length: 1000,
                    Width: 100,
                    Height: 100,
                    Role: i == 0 ? DuplicateRole.Keeper : DuplicateRole.Redundant,
                    Distance: i)),
            ]));

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

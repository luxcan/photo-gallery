using PhotoGallery.App.Duplicates;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Choosing which copy of a duplicated picture survives.
/// </summary>
/// <remarks>
/// The control is a checkbox but behaves like a radio that can be cleared, and
/// that behaviour is written here rather than handed to WPF. Both halves matter:
/// a second tick must not mean a second keeper, and unticking the last one must
/// lift the group back out of the decision so a screen-wide Delete cannot reach
/// it.
/// </remarks>
public sealed class DuplicateSelectionTests
{
    [Fact]
    public void Ticking_ASecondCopyUnticksTheFirst()
    {
        DuplicateSetItem set = SetOf(3);

        set.Copies[0].IsKept = true;
        set.Copies[2].IsKept = true;

        Assert.Equal([false, false, true], set.Copies.Select(copy => copy.IsKept));
        Assert.Single(set.Kept);
    }

    [Fact]
    public void Ticking_LeavesTheOthersToBeDeleted()
    {
        DuplicateSetItem set = SetOf(3);

        set.Copies[1].IsKept = true;

        Assert.Equal(2, set.Doomed.Count);
        Assert.DoesNotContain(set.Copies[1], set.Doomed);
    }

    [Fact]
    public void NothingIsTickedUntilSomebodyTicksIt()
    {
        // An untouched group is not part of what any Delete button does, which
        // is what makes a button acting on the whole screen safe.
        DuplicateSetItem set = SetOf(3);

        Assert.All(set.Copies, copy => Assert.False(copy.IsKept));
        Assert.False(set.CanDelete);
    }

    [Fact]
    public void Unticking_TheLastOneTakesTheGroupBackOutOfTheDecision()
    {
        // The reason these are not radio buttons: a radio cannot be un-pressed,
        // so one stray click could never be taken back.
        DuplicateSetItem set = SetOf(2);
        set.Copies[0].IsKept = true;
        Assert.True(set.CanDelete);

        set.Copies[0].IsKept = false;

        Assert.False(set.CanDelete);
        Assert.Empty(set.Kept);
    }

    [Fact]
    public void Ticking_SaysWhatTheButtonWouldDo()
    {
        DuplicateSetItem set = SetOf(3);
        Assert.Equal("Tick the copy you want to keep", set.DeleteCaption);

        set.Copies[0].IsKept = true;

        Assert.Contains("Keep this one and delete the other 2", set.DeleteCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void Ticking_ThroughEveryCopyInTurnNeverLeavesTwoKept()
    {
        // Guards the un-ticking from being taken for a fresh choice, which would
        // have each copy clear the one before it in a loop.
        DuplicateSetItem set = SetOf(4);

        foreach (DuplicateCopyItem copy in set.Copies)
        {
            copy.IsKept = true;
            Assert.Single(set.Kept);
        }

        Assert.Same(set.Copies[^1], set.Kept[0]);
    }

    [Fact]
    public void Ticking_SeveralIsAllowedWhereTheAppIsOnlyGuessing()
    {
        // A burst of shots seconds apart lands in one visually-alike group, and
        // several of them can be photographs worth having. Only identical copies
        // are held to one.
        DuplicateSetItem set = SetOf(3, DuplicateKind.Near);

        set.Copies[0].IsKept = true;
        set.Copies[1].IsKept = true;

        Assert.Equal(2, set.Kept.Count);
        Assert.Single(set.Doomed);
        Assert.Contains("Keep these 2 and delete the other", set.DeleteCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void Ticking_EveryCopyLeavesNothingToDelete()
    {
        // Not a failure state - it is somebody saying the group is not a
        // duplicate at all. The button stands down and says so.
        DuplicateSetItem set = SetOf(2, DuplicateKind.Near);

        set.Copies[0].IsKept = true;
        set.Copies[1].IsKept = true;

        Assert.False(set.CanDelete);
        Assert.Equal("Keeping every copy", set.DeleteCaption);
    }

    private static DuplicateSetItem SetOf(
        int copies, DuplicateKind kind = DuplicateKind.Exact) =>
        new(new DuplicateSetView(
            1,
            kind,
            [
                .. Enumerable.Range(0, copies).Select(i => new DuplicateCopy(
                    AssetId: i + 1,
                    PhotoSourceId: 1,
                    RelativePath: $@"20230201\photo-{i}.jpg",
                    FullPath: $@"C:\pictures\20230201\photo-{i}.jpg",
                    ThumbnailName: "thumb.jpg",
                    Length: 1000,
                    Width: 100,
                    Height: 100,
                    Role: i == 0 ? DuplicateRole.Keeper : DuplicateRole.Redundant,
                    Distance: 0)),
            ]));
}

using PhotoGallery.App.Duplicates;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What each card on the "Looks the same" screen says about why a copy is
/// suggested or not (PRP 14), and where the closed marker goes.
/// </summary>
public sealed class DuplicateCaptionTests
{
    [Theory]
    [InlineData(KeeperCriterion.Eyes, "Suggested: eyes open")]
    [InlineData(KeeperCriterion.Sharpness, "Suggested: sharpest")]
    [InlineData(KeeperCriterion.Exposure, "Suggested: best exposed")]
    [InlineData(KeeperCriterion.Original, "Suggested: original file")]
    [InlineData(KeeperCriterion.Pixels, "Suggested: largest picture")]
    public void TheSuggestedCopyNamesTheRungThatChoseIt(KeeperCriterion decidedBy, string caption) =>
        Assert.Equal(caption, new DuplicateCopyItem(Copy(DuplicateRole.Keeper, decidedBy)).RoleCaption);

    [Fact]
    public void ACopyBeatenOnEyesNamesWhoseEyesAreClosed() =>
        Assert.Equal(
            "Mum's eyes are closed",
            new DuplicateCopyItem(Copy(DuplicateRole.Redundant, KeeperCriterion.Eyes, closed: [Face(1, "Mum")]))
                .RoleCaption);

    [Fact]
    public void TwoOrMorePeopleAreCountedRatherThanListed() =>
        Assert.Equal(
            "2 people's eyes are closed",
            new DuplicateCopyItem(Copy(
                DuplicateRole.Redundant, KeeperCriterion.Eyes, closed: [Face(1, "Mum"), Face(2, "Ana")]))
                .RoleCaption);

    [Fact]
    public void OnePersonWithTwoFacesIsStillOnePerson() =>
        Assert.Equal(
            "Mum's eyes are closed",
            new DuplicateCopyItem(Copy(
                DuplicateRole.Redundant, KeeperCriterion.Eyes, closed: [Face(1, "Mum"), Face(2, "Mum")]))
                .RoleCaption);

    [Theory]
    [InlineData(KeeperCriterion.Sharpness, ExposureFault.None, "Blurred")]
    [InlineData(KeeperCriterion.Exposure, ExposureFault.TooBright, "Too bright")]
    [InlineData(KeeperCriterion.Exposure, ExposureFault.TooDark, "Too dark")]
    [InlineData(KeeperCriterion.Original, ExposureFault.None, "Re-saved copy")]
    public void ACopyBeatenSaysWhatIsWrongWithIt(KeeperCriterion lostOn, ExposureFault exposure, string caption) =>
        Assert.Equal(caption, new DuplicateCopyItem(Copy(DuplicateRole.Redundant, lostOn, exposure)).RoleCaption);

    [Fact]
    public void ACopyBeatenOnSizeSaysNothingExtra() =>
        Assert.Equal(
            "3 of 64 bits differ",
            new DuplicateCopyItem(Copy(DuplicateRole.Redundant, KeeperCriterion.Pixels)).RoleCaption);

    [Fact]
    public void ASetWithNothingSuggestedSaysSo() =>
        Assert.Equal(
            "Nothing to choose between these",
            new DuplicateSetItem(new DuplicateSetView(1, DuplicateKind.Near,
                [Copy(DuplicateRole.Undecided, null), Copy(DuplicateRole.Undecided, null)])).Verdict);

    [Fact]
    public void AnIdenticalSetSaysItIsTheSameFile() =>
        Assert.Equal(
            "These are the same file",
            new DuplicateSetItem(new DuplicateSetView(1, DuplicateKind.Exact,
                [Copy(DuplicateRole.Undecided, null), Copy(DuplicateRole.Undecided, null)])).Verdict);

    [Fact]
    public void ASetWithASuggestionHasNoVerdictLine() =>
        Assert.False(new DuplicateSetItem(new DuplicateSetView(1, DuplicateKind.Near,
            [Copy(DuplicateRole.Keeper, KeeperCriterion.Sharpness), Copy(DuplicateRole.Redundant, KeeperCriterion.Sharpness)]))
            .HasVerdict);

    [Fact]
    public void TheClosedMarkerSitsOnTheFaceThatCostTheCopy()
    {
        var item = new DuplicateCopyItem(Copy(
            DuplicateRole.Redundant, KeeperCriterion.Eyes, closed: [Face(7, "Mum", new FaceBounds(300, 120, 90, 90))]));

        Assert.True(item.HasClosedMarkers);
        Assert.Equal(new FaceBounds(300, 120, 90, 90), Assert.Single(item.ClosedMarkers));
        Assert.Equal(1024, item.MarkerSpaceWidth);
        Assert.Equal(768, item.MarkerSpaceHeight);
    }

    [Fact]
    public void ACopyNotBeatenOnEyesHasNoMarker() =>
        Assert.False(new DuplicateCopyItem(Copy(DuplicateRole.Keeper, KeeperCriterion.Eyes)).HasClosedMarkers);

    private static DuplicateCopy Copy(
        DuplicateRole role,
        KeeperCriterion? decidedBy,
        ExposureFault exposure = ExposureFault.None,
        IReadOnlyList<CopyFace>? closed = null) =>
        new(
            AssetId: 1, PhotoSourceId: 1, RelativePath: @"a\b.jpg", FullPath: @"C:\a\b.jpg",
            ThumbnailName: null, Length: 1000, Width: 4000, Height: 3000, Role: role, Distance: 3,
            DecidedBy: decidedBy, Exposure: exposure, ClosedFaces: closed ?? [],
            PreviewWidth: 1024, PreviewHeight: 768);

    private static CopyFace Face(int faceId, string name, FaceBounds? bounds = null) =>
        new(faceId, faceId, name, bounds ?? new FaceBounds(0, 0, 50, 50), EyeState.Closed, null);
}

using PhotoGallery.App.Models;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Models;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The parts of PRP 14 that live in markup and on the Settings card, read as
/// text because a WPF binding to a name that does not exist fails silently.
/// </summary>
public sealed class BestOfABurstMarkupTests
{
    private static readonly string s_window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    [Fact]
    public void LookingForDuplicatesGoesThroughTheShellsOverlay()
    {
        // The measuring takes minutes the first time, so the button runs the
        // shell's command, which names the step and offers Stop.
        Assert.Contains("Command=\"{Binding FindDuplicatesCommand}\"", s_window, StringComparison.Ordinal);
        Assert.DoesNotContain("Duplicates.FindCommand", s_window, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{Binding RoleCaption}")]
    [InlineData("{Binding Verdict}")]
    [InlineData("{Binding HasVerdict,")]
    [InlineData("{Binding ClosedMarkers}")]
    [InlineData("{Binding HasClosedMarkers,")]
    [InlineData("{Binding MarkerSpaceWidth}")]
    [InlineData("{Binding MarkerSpaceHeight}")]
    public void TheCardsShowWhyAndWhere(string binding) =>
        Assert.Contains(binding, s_window, StringComparison.Ordinal);

    [Fact]
    public void TheSuggestedCopyCarriesARibbonAcrossItsCorner()
    {
        // The reason under the card is small print; the ribbon is what makes the
        // suggestion visible across a screenful of sets.
        Assert.Contains("Style=\"{DynamicResource SuggestedRibbon}\"", s_window, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding IsKeeper,", s_window, StringComparison.Ordinal);

        string controls = File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));
        Assert.Contains("x:Key=\"SuggestedRibbon\"", controls, StringComparison.Ordinal);
        Assert.Contains("<RotateTransform Angle=\"-45\" />", controls, StringComparison.Ordinal);

        // Cut at the photograph's edges, not the tile's: the picture's grid is
        // centred so it shrinks to the picture as drawn, and clips the ribbon.
        Assert.Matches(
            "<Grid x:Name=\"CopyPicture\" ClipToBounds=\"True\"\\s+HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\">",
            s_window);
    }

    [Fact]
    public void TheCreditsSayThePackAlsoReadsEyes() =>
        Assert.Contains("tell whether their eyes are open", s_window, StringComparison.Ordinal);

    [Fact]
    public void TheEyeFeatureHasItsOwnCardWithItsLicence()
    {
        FeatureCard card = FeatureCard.Of(new FeatureStatus(
            ModelFeature.Eyes,
            [new ModelFileStatus(ModelId.FaceLandmarks, "2d106det.onnx", 5_030_888, "Research only.", ModelState.Missing)]));

        Assert.Equal("Telling whether eyes are open", card.Title);
        Assert.Equal("Research only.", card.Licence);
        Assert.True(card.CanDownload);
    }

    [Fact]
    public void ASingleFileIsNotCountedAsAll()
    {
        // "Download all 1 files" is what the count alone produced.
        FeatureCard card = FeatureCard.Of(new FeatureStatus(
            ModelFeature.Eyes,
            [new ModelFileStatus(ModelId.FaceLandmarks, "2d106det.onnx", 5_030_888, "Research only.", ModelState.Missing)]));

        Assert.Equal("Download the file", card.DownloadLabel);
    }

    [Fact]
    public void TheEyeFileIsNotPartOfFindingPeople()
    {
        // In the faces list it would keep People closed on every computer that
        // has not imported it.
        Assert.DoesNotContain(ModelId.FaceLandmarks, FeatureModels.Of(ModelFeature.Faces));
        Assert.Equal([ModelId.FaceLandmarks], FeatureModels.Of(ModelFeature.Eyes));
    }
}

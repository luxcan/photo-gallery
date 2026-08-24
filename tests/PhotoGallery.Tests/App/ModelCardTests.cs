using PhotoGallery.App.Models;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Models;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What one feature's card on the Settings screen offers to download.
/// </summary>
/// <remarks>
/// The card carries both ways of fetching the files: a link per file, for one
/// that went astray, and a button that fetches everything still wanted. What
/// matters here is that the button never offers what is already installed - a
/// user coming back for the last file should not be invited to start 1.2 GB
/// again - and that it says how much it is about to open.
/// </remarks>
public sealed class ModelCardTests
{
    [Fact]
    public void NothingInstalled_OffersEveryFile()
    {
        FeatureCard card = Card(ModelFeature.ContentSearch, ready: 0);

        Assert.True(card.CanDownload);
        Assert.Equal(4, card.Downloads.Count);
        Assert.Equal("Download all 4 files", card.DownloadLabel);
    }

    [Fact]
    public void APairIsNamedAsAPair()
    {
        // "Download all 2 files" is what a count alone would produce, and it
        // reads like a machine wrote it.
        Assert.Equal("Download both files", Card(ModelFeature.Faces, ready: 0).DownloadLabel);
    }

    [Fact]
    public void PartlyInstalled_OffersOnlyWhatIsMissing()
    {
        FeatureCard card = Card(ModelFeature.ContentSearch, ready: 2);

        Assert.Equal(2, card.Downloads.Count);
        Assert.Equal("Download the 2 remaining files", card.DownloadLabel);
        Assert.All(
            card.Downloads,
            url => Assert.DoesNotContain("visual/model.onnx", url, StringComparison.Ordinal));
    }

    [Fact]
    public void OneFileLeft_IsSaidInTheSingular()
    {
        FeatureCard card = Card(ModelFeature.Faces, ready: 1);

        Assert.Single(card.Downloads);
        Assert.Equal("Download the remaining file", card.DownloadLabel);
    }

    [Fact]
    public void Installed_OffersNothing()
    {
        FeatureCard card = Card(ModelFeature.Faces, ready: 2);

        Assert.False(card.CanDownload);
        Assert.Empty(card.Downloads);
    }

    [Fact]
    public void EveryFileTheAppWantsHasSomewhereToComeFrom()
    {
        // A card with a file it cannot name a source for would show a plain row
        // and a button that quietly skips it.
        Assert.All(FeatureModels.All, id => Assert.False(string.IsNullOrWhiteSpace(ModelSources.Of(id))));
    }

    /// <summary>A feature whose first <paramref name="ready"/> files are installed.</summary>
    private static FeatureCard Card(ModelFeature feature, int ready)
    {
        IReadOnlyList<ModelId> files = FeatureModels.Of(feature);

        return FeatureCard.Of(new FeatureStatus(
            feature,
            [.. files.Select((id, index) => new ModelFileStatus(
                id,
                $"{id}.onnx".ToLowerInvariant(),
                Bytes: 1024 * (index + 1),
                Licence: "Test terms.",
                State: index < ready ? ModelState.Ready : ModelState.Missing))]));
    }
}

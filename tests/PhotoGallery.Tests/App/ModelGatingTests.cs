using System.Globalization;
using System.Windows;
using PhotoGallery.App.Shell;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What the side nav does about a feature whose model has not been downloaded.
/// </summary>
/// <remarks>
/// Two gates, not one, and they are not interchangeable. A section can be
/// waiting on photographs, which the user can add here and now, or on a model,
/// which they have to go and fetch - so the reason has to send them to different
/// places. Photographs are named first, because a library with none cannot use a
/// model even once it has one.
/// </remarks>
public sealed class ModelGatingTests
{
    private readonly SectionEnabledConverter _enabled = new();
    private readonly SectionToolTipConverter _tip = new();

    [Theory]
    [InlineData(true, true, true, true, true)]      // everything present
    [InlineData(true, false, false, true, false)]   // no photographs
    [InlineData(true, true, true, false, false)]    // photographs, no model
    [InlineData(false, false, false, false, true)]  // needs neither
    [InlineData(false, false, true, false, false)]  // needs only the model, and has not got it
    public void ASectionIsEnabledOnlyWhenEverythingItNeedsIsThere(
        bool requiresSources,
        bool hasSources,
        bool requiresFaces,
        bool facesAvailable,
        bool expected) =>
        Assert.Equal(
            expected,
            _enabled.Convert(
                [requiresSources, hasSources, requiresFaces, facesAvailable],
                typeof(bool),
                null,
                CultureInfo.InvariantCulture));

    [Fact]
    public void TwoValuesStillMeanTheOldTwoValueRule()
    {
        // A caller written before there was a model to wait for keeps working,
        // rather than every section going dark because two bindings are absent.
        Assert.Equal(
            true,
            _enabled.Convert([true, true], typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AMissingModel_SendsTheUserToSettings()
    {
        Assert.Equal(
            "People - install the face model in Settings first",
            Tip(requiresSources: true, hasSources: true, requiresFaces: true, faces: false));
    }

    [Fact]
    public void MissingPhotographs_AreNamedBeforeAMissingModel()
    {
        // Both are wrong, and only one of them is worth doing first: downloading
        // 182 MB achieves nothing on a library with no photographs in it.
        Assert.Equal(
            "People - add a photo folder in Library first",
            Tip(requiresSources: true, hasSources: false, requiresFaces: true, faces: false));
    }

    [Fact]
    public void AnAvailableFeature_HasNothingToExplain()
    {
        Assert.Equal(
            DependencyProperty.UnsetValue,
            Tip(requiresSources: true, hasSources: true, requiresFaces: true, faces: true));
    }

    [Fact]
    public void FourValues_StillMeanNoModelGate()
    {
        // The form the tooltip had before models gated anything: it must not
        // start claiming a model is missing because nobody told it otherwise.
        Assert.Equal(
            DependencyProperty.UnsetValue,
            _tip.Convert(
                ["People", true, true, false], typeof(object), null, CultureInfo.InvariantCulture));
    }

    private object? Tip(bool requiresSources, bool hasSources, bool requiresFaces, bool faces) =>
        _tip.Convert(
            ["People", requiresSources, hasSources, false, requiresFaces, faces],
            typeof(object),
            null,
            CultureInfo.InvariantCulture);
}

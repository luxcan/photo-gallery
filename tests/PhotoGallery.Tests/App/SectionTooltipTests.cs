using System.Globalization;
using System.Windows;
using PhotoGallery.App.Shell;

namespace PhotoGallery.Tests.App;

/// <summary>
/// One tooltip carrying two different jobs. Folded, it is the name the icon
/// cannot show; disabled, it is the reason the item will not respond - and that
/// has to be said whether the nav is folded or not, because it is the answer to
/// a click that did nothing.
/// </summary>
public sealed class SectionTooltipTests
{
    private readonly SectionToolTipConverter _converter = new();

    [Fact]
    public void ADisabledSection_SaysWhyInBothStates()
    {
        const string expected = "Library - add a photo folder in Library first";

        Assert.Equal(expected, Tip("Library", requiresSources: true, hasSources: false, folded: true));
        Assert.Equal(expected, Tip("Library", requiresSources: true, hasSources: false, folded: false));
    }

    [Fact]
    public void AFoldedSection_IsNamedByItsTooltip()
    {
        Assert.Equal(
            "Duplicates",
            Tip("Duplicates", requiresSources: true, hasSources: true, folded: true));
    }

    [Fact]
    public void AnOpenEnabledSection_HasNoTooltipAtAll()
    {
        // UnsetValue rather than an empty string, so the target keeps its own
        // default and no empty box is drawn.
        Assert.Equal(
            DependencyProperty.UnsetValue,
            Tip("Settings", requiresSources: false, hasSources: true, folded: false));
    }

    [Fact]
    public void ThreeValues_StillMeanAlwaysNameIt()
    {
        // What a caller written against the icon-only bar assumed, kept so that
        // dropping the fourth binding degrades rather than blanks.
        Assert.Equal(
            "People",
            _converter.Convert([ "People", true, true ], typeof(object), null, CultureInfo.InvariantCulture));
    }

    private object? Tip(string title, bool requiresSources, bool hasSources, bool folded) =>
        _converter.Convert(
            [title, requiresSources, hasSources, folded],
            typeof(object),
            null,
            CultureInfo.InvariantCulture);
}

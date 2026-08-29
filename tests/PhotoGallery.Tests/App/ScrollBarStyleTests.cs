using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The scrollbars are the app's own, and every ScrollViewer gets them.
/// </summary>
/// <remarks>
/// Both palettes carried ScrollBarTrack, ScrollBarThumb and ScrollBarThumbHover
/// from the day the theme landed, and for as long as that, nothing consumed
/// them: six brushes defined in parity, imported from the handoff, referenced by
/// nobody. What every ScrollViewer in the app actually drew was Windows' own grey
/// chrome with arrow buttons, pale on a dark panel, and it went unnoticed because
/// a colour that is never asked for fails silently.
///
/// <para>Nothing about that is behaviour, so nothing in a view-model suite could
/// have caught it. These are the three things that, had they been asserted,
/// would have.</para>
/// </remarks>
public sealed class ScrollBarStyleTests
{
    private static readonly XNamespace s_wpf =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace s_x = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("ScrollBarTrack")]
    [InlineData("ScrollBarThumb")]
    [InlineData("ScrollBarThumbHover")]
    public void TheScrollBarBrushesAreActuallyUsed(string brush)
    {
        Assert.Contains(
            $"{{DynamicResource {brush}}}",
            Controls(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Keyless, so it reaches every ScrollViewer there is.
    /// </summary>
    /// <remarks>
    /// The point of the implicit style: most of the scrollbars in this app belong
    /// to controls whose templates nobody here wrote - the ListView table, the
    /// ComboBox popup, a TextBox that overflows. Giving this style a key would
    /// leave every one of those Windows-drawn again, and the app would look
    /// exactly as it did before, which is a change nobody would think to test.
    /// </remarks>
    [Fact]
    public void TheScrollBarStyleIsImplicit()
    {
        XElement? style = Markup()
            .Root!
            .Elements(s_wpf + "Style")
            .FirstOrDefault(element =>
                (string?)element.Attribute("TargetType") == "{x:Type ScrollBar}");

        Assert.True(style is not null, "No ScrollBar style found; the app is back on Windows' chrome.");
        Assert.Null(style!.Attribute(s_x + "Key"));
    }

    /// <summary>
    /// No arrow buttons, which is the half of the default chrome that gives it
    /// away at a glance.
    /// </summary>
    [Fact]
    public void ScrollBarsHaveNoArrowButtons()
    {
        string controls = Controls();

        Assert.DoesNotContain("ScrollBar.LineUpCommand", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollBar.LineDownCommand", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollBar.LineLeftCommand", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollBar.LineRightCommand", controls, StringComparison.Ordinal);
    }

    private static string Controls() =>
        File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));

    private static XDocument Markup() =>
        XDocument.Load(AppMarkup.PathTo("Theme", "Controls.xaml"));
}

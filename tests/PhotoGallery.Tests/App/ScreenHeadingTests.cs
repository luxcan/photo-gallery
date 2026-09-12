using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// That a screen names itself the same way wherever you are.
/// </summary>
/// <remarks>
/// The app had three answers at once: Settings, Sharing and the set-up screen
/// shouted at 28 and Bold, the About card had been overridden by hand to 20 and
/// SemiBold, and the Albums wall called itself in the field-label style at 12.5.
/// The handoff settles it at 20 SemiBold, which is what About had already worked
/// out on its own.
///
/// <para>An override at one use is how that came apart the first time, so the
/// rule is not only that the shared style holds the value but that no screen
/// sets the size or the weight beside it.</para>
/// </remarks>
public sealed class ScreenHeadingTests
{
    private static readonly XNamespace s_x = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TheSharedHeadingIsTwentyAndSemiBold()
    {
        XElement heading = XDocument.Load(AppMarkup.PathTo("Theme", "Controls.xaml"))
            .Descendants()
            .Single(e => e.Name.LocalName == "Style"
                         && (string?)e.Attribute(s_x + "Key") == "Heading");

        Assert.Equal("20", Value(heading, "FontSize"));
        Assert.Equal("SemiBold", Value(heading, "FontWeight"));
    }

    [Theory]
    [InlineData("Shell", "MainWindow.xaml")]
    [InlineData("Shell", "WelcomeWindow.xaml")]
    public void NoScreenResizesItsOwnHeading(params string[] file)
    {
        List<XElement> headings = [.. XDocument.Load(AppMarkup.PathTo(file))
            .Descendants()
            .Where(e => (string?)e.Attribute("Style") == "{DynamicResource Heading}")];

        Assert.NotEmpty(headings);

        foreach (XElement heading in headings)
        {
            Assert.Null(heading.Attribute("FontSize"));
            Assert.Null(heading.Attribute("FontWeight"));
        }
    }

    [Fact]
    public void TheAlbumsWallNamesItselfInIt()
    {
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        Assert.Contains(
            "Style=\"{DynamicResource Heading}\" Margin=\"0\" Text=\"Albums\"",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheSentenceUnderItIsGone()
    {
        // It explained that an album never moves or renames a file. True, and
        // read once: the handoff takes it out, and what the screen says in that
        // spot now is what just happened. The collection panel still says the
        // same thing about a shelf, which is the first time anybody meets it,
        // so the sentence is matched here rather than the phrase.
        Assert.DoesNotContain(
            "Nothing here moves or renames a file. A photograph belongs to one album",
            File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml")),
            StringComparison.Ordinal);
    }

    private static string? Value(XElement style, string property) =>
        (string?)style.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Setter"
                                 && (string?)e.Attribute("Property") == property)
            ?.Attribute("Value");
}

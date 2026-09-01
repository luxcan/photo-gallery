using System.Xml;
using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Everything that floats over the app is drawn on the one surface, read against
/// the one shade, and titled in the one size.
/// </summary>
/// <remarks>
/// Controls.xaml has claimed as much in a comment since the surface was written,
/// and the comment was already untrue of most of the app. The message box
/// borrowed Heading, which is 28px and bold and is what the Settings page titles
/// itself with, while the pass overlay said 16. The two pickers hand-rolled a
/// border rather than using the shared one. The two album panels dimmed with a
/// literal 60% black while everything else used the shared shade, so a modal was
/// a different darkness depending on which one it was. And the message box, being
/// a window of its own rather than a panel inside one, dimmed nothing at all and
/// read as a panel that had come loose.
///
/// <para>Every one of those passed a fully green suite, because none of them is
/// behaviour. A rule that only a comment states is a rule that drifts, so this is
/// the comment as a failing test.</para>
/// </remarks>
public sealed class ModalParityTests
{
    private static readonly XNamespace s_wpf =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace s_x = "http://schemas.microsoft.com/winfx/2006/xaml";

    private const string Surface = "{DynamicResource ModalSurface}";
    private const string Scrim = "{DynamicResource ModalScrim}";
    private const string TitleStyle = "{DynamicResource ModalTitle}";

    /// <summary>What gates the long pass, which is the one modal Escape leaves alone.</summary>
    private const string Running = "IsOverlayVisible";

    /// <summary>
    /// A panel centred in both directions is floating over something, whatever
    /// it holds - which is the one thing every modal here has in common, and the
    /// only one a reader of the markup can be sure of.
    /// </summary>
    [Fact]
    public void EveryCentredPanelIsDrawnOnTheSharedSurface()
    {
        List<string> strays = [];

        foreach ((string file, XDocument document) in Markup())
        {
            foreach (XElement border in document.Descendants(s_wpf + "Border"))
            {
                bool centred = (string?)border.Attribute("HorizontalAlignment") == "Center"
                            && (string?)border.Attribute("VerticalAlignment") == "Center";

                if (centred && (string?)border.Attribute("Style") != Surface)
                {
                    strays.Add($"{file}:{LineOf(border)} is centred over the app "
                             + "but is not on ModalSurface");
                }
            }
        }

        Assert.True(strays.Count == 0, string.Join("\n", strays));
    }

    /// <summary>
    /// Guards the test above against passing because a moved folder left it with
    /// nothing to check.
    /// </summary>
    [Fact]
    public void ThereAreModalsToCheck()
    {
        int surfaces = Markup()
            .SelectMany(pair => pair.Document.Descendants())
            .Count(element => (string?)element.Attribute("Style") == Surface);

        Assert.True(surfaces >= 5, $"Expected the shared surface to be in use; found {surfaces}.");
    }

    /// <summary>
    /// The shade is a resource so that every modal is the same darkness. Written
    /// as a colour instead, it stops being.
    /// </summary>
    [Fact]
    public void NoModalIsDimmedWithAColourOfItsOwn()
    {
        List<string> strays = [];

        foreach ((string file, XDocument document) in Markup())
        {
            foreach (XElement surface in Surfaces(document))
            {
                foreach (XElement ancestor in surface.Ancestors())
                {
                    string? background = (string?)ancestor.Attribute("Background");

                    if (background is not null && background.StartsWith('#'))
                    {
                        strays.Add($"{file}:{LineOf(ancestor)} dims a modal with "
                                 + $"{background} rather than ModalScrim");
                    }
                }
            }
        }

        Assert.True(strays.Count == 0, string.Join("\n", strays));
    }

    /// <summary>
    /// And that the shade is the thing actually doing the dimming, rather than
    /// the resource surviving while every modal quietly stopped using it.
    /// </summary>
    [Fact]
    public void TheSharedShadeIsWhatDims()
    {
        int dimmed = Markup()
            .SelectMany(pair => pair.Document.Descendants())
            .Count(element => (string?)element.Attribute("Background") == Scrim);

        Assert.True(dimmed >= 3, $"Expected the shared shade to be dimming; found {dimmed}.");
    }

    /// <summary>
    /// The dialog is a window, so its shade cannot be a border above it in the
    /// markup and is raised in code instead. It is still the same shade.
    /// </summary>
    [Fact]
    public void TheDialogRaisesTheSameShade()
    {
        Assert.Contains("ModalScrim", Source("Shell", "ScrimAdorner.cs"), StringComparison.Ordinal);

        string dialog = Source("Shell", "AppDialog.xaml.cs");

        Assert.Contains("ScrimAdorner.Cover", dialog, StringComparison.Ordinal);

        // The other way in would be a dialog shown without the shade, which is
        // exactly what these calls looked like before there was one.
        Assert.DoesNotContain("dialog.ShowDialog()", dialog, StringComparison.Ordinal);
    }

    /// <summary>
    /// The dialog and the pass overlay ask for attention in the same voice.
    /// </summary>
    [Fact]
    public void ModalTitlesAgreeOnTheirSize()
    {
        XElement dialog = Title(
            "AppDialog.xaml", e => (string?)e.Attribute(s_x + "Name") == "TitleText");

        XElement overlay = Title(
            "MainWindow.xaml", e => (string?)e.Attribute("Text") == "{Binding OverlayTitle}");

        foreach (XElement title in new[] { dialog, overlay })
        {
            Assert.Equal(TitleStyle, (string?)title.Attribute("Style"));
            Assert.Null(title.Attribute("FontSize"));
        }
    }

    /// <summary>
    /// Heading is the style a page titles itself with. A dialog that borrows it
    /// arrives in the same type as the screen behind it, which is where this
    /// started.
    /// </summary>
    [Fact]
    public void NoModalTitlesItselfWithTheHeadingStyle()
    {
        Assert.DoesNotContain(
            "{DynamicResource Heading}",
            Source("Shell", "AppDialog.xaml"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every panel floating over the app can be put down with Escape.
    /// </summary>
    /// <remarks>
    /// The lists drawn over a picture always could, because each screen's key
    /// handler carried a branch for its own. The two album panels have no such
    /// handler and so could only be left by finding their Cancel button, which
    /// on a short window is below the fold of a panel whose bottom the user
    /// cannot see. Escape is now decided in one place for all of them.
    ///
    /// <para>Found rather than listed, so a fourth panel arrives here on the day
    /// it is written. The lists themselves are checked the same way in
    /// <c>ViewerFocusTests</c> - their surfaces live in templates rather than in
    /// this window, so they are found by shape instead of by markup.</para>
    /// </remarks>
    [Fact]
    public void EveryModalPanelIsClosedByEscape()
    {
        string list = DismissibleList();
        List<string> stuck = [];
        int considered = 0;

        Assert.True(
            list.Length > 0,
            "Dismissible has been renamed or removed; nothing decides what Escape closes.");

        foreach (string opener in ModalOpeners())
        {
            if (opener == Running)
            {
                continue;
            }

            considered++;
            if (!list.Contains(opener, StringComparison.OrdinalIgnoreCase))
            {
                stuck.Add($"{opener} opens a panel over the app that Escape cannot close. "
                        + "Add it to Dismissible in MainWindow.");
            }
        }

        Assert.True(
            considered > 0, "No dismissible panel was found; the test has lost its subject.");
        Assert.True(stuck.Count == 0, string.Join("\n", stuck));
    }

    /// <summary>The body of the one method that says what Escape closes.</summary>
    private static string DismissibleList()
    {
        string window = Source("Shell", "MainWindow.xaml.cs");

        // The declaration, not the call above it: the call reads "in Dismissible()".
        int start = window.IndexOf("> Dismissible()", StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        int end = window.IndexOf("\n    private ", start, StringComparison.Ordinal);

        return end < 0 ? window[start..] : window[start..end];
    }

    /// <summary>
    /// The one panel Escape may not close is still the one that is working.
    /// </summary>
    /// <remarks>
    /// A long pass is not a question waiting to be answered. It carries a Stop
    /// button that names what stopping costs, and one of those passes deletes
    /// photographs - abandoning it halfway should take more than a keystroke
    /// meant for something else. This exists so the exclusion stays a decision
    /// rather than becoming an oversight nobody can date.
    /// </remarks>
    [Fact]
    public void TheRunningPassIsTheDeliberateException()
    {
        Assert.Contains(Running, ModalOpeners());
        Assert.DoesNotContain(Running, DismissibleList(), StringComparison.Ordinal);
    }

    /// <summary>What each modal in this window is gated by, as a binding path.</summary>
    private static IEnumerable<string> ModalOpeners()
    {
        XDocument window = Markup()
            .Single(pair => pair.File.EndsWith("MainWindow.xaml", StringComparison.Ordinal))
            .Document;

        foreach (XElement surface in Surfaces(window))
        {
            string? gate = surface.AncestorsAndSelf()
                .Select(element => (string?)element.Attribute("Visibility"))
                .FirstOrDefault(value =>
                    value is not null && value.Contains("{Binding", StringComparison.Ordinal));

            if (gate is not null)
            {
                yield return BindingPath(gate);
            }
        }
    }

    /// <summary>The property a <c>{Binding Some.Path, Converter=...}</c> reads.</summary>
    private static string BindingPath(string binding)
    {
        int start = binding.IndexOf("{Binding", StringComparison.Ordinal) + "{Binding".Length;
        int end = binding.IndexOfAny([',', '}'], start);

        return binding[start..(end < 0 ? binding.Length : end)].Trim();
    }

    private static XElement Title(string fileName, Func<XElement, bool> isTitle)
    {
        XDocument document = Markup()
            .Single(pair => pair.File.EndsWith(fileName, StringComparison.Ordinal))
            .Document;

        XElement? title = document.Descendants(s_wpf + "TextBlock").SingleOrDefault(isTitle);

        Assert.True(title is not null, $"No title found in {fileName}; the test has lost its subject.");
        return title!;
    }

    private static IEnumerable<XElement> Surfaces(XDocument document) =>
        document.Descendants(s_wpf + "Border")
            .Where(border => (string?)border.Attribute("Style") == Surface);

    private static IEnumerable<(string File, XDocument Document)> Markup() =>
        new[]
        {
            Path.Combine("Theme", "Controls.xaml"),
            Path.Combine("Shell", "MainWindow.xaml"),
            Path.Combine("Shell", "AppDialog.xaml"),
        }
        .Select(relative => (relative,
            XDocument.Load(AppMarkup.PathTo(relative), LoadOptions.SetLineInfo)));

    private static string Source(params string[] relative) =>
        File.ReadAllText(AppMarkup.PathTo(relative));

    private static int LineOf(XElement element) => ((IXmlLineInfo)element).LineNumber;
}

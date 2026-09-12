using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// That no screen keeps a colour of its own.
/// </summary>
/// <remarks>
/// A palette swap only reaches a screen that asked the palette. One
/// <c>Background="#..."</c> written in a hurry survives every re-skin the app
/// will ever have, looks perfectly correct on the theme it was written against,
/// and is invisible until somebody switches - which is how a teal artefact would
/// outlive the teal theme.
///
/// <para><c>Controls.xaml</c> is the trap rather than the exception:
/// <c>App.xaml</c> merges the palette at slot 0 and <c>ThemeManager</c> replaces
/// that slot, so the control file is never swapped and a literal added there is
/// theme-blind for good.</para>
///
/// <para>The four allowed literals are all chrome drawn over a photograph, where
/// a dark scrim with white on it is right in both themes and following the page
/// would be wrong. They are listed one by one rather than waved through by
/// pattern, so a fifth has to be argued for here.</para>
/// </remarks>
public sealed class ThemeUsageTests
{
    /// <summary>Every markup file that dresses a screen. The palettes are not here.</summary>
    private static readonly string[][] s_screens =
    [
        ["Shell", "MainWindow.xaml"],
        ["Shell", "AppDialog.xaml"],
        ["Shell", "WelcomeWindow.xaml"],
        ["App.xaml"],
        ["Theme", "Controls.xaml"],
    ];

    /// <summary>
    /// The colours a screen may hold itself, and why. Each is a scrim over a
    /// picture, or the text on one.
    /// </summary>
    private static readonly Dictionary<string, string> s_allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#CC1E1E1E"] = "the modal shade, the face label chip and the crop overlay",
        ["#99000000"] = "the play button drawn on a video still",
        ["#CC000000"] = "the scrub bar and the playback error, over the film",
        ["White"] = "text on those scrims",
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void NoScreenPaintsAColourOfItsOwn(string file)
    {
        List<string> offenders = [.. ColoursIn(file).Where(c => !s_allowed.ContainsKey(c)).Distinct()];

        Assert.True(
            offenders.Count == 0,
            $"{file} paints {string.Join(", ", offenders)} itself. "
            + "Name a brush in Light.xaml and Dark.xaml instead, or add it above "
            + "with the reason it cannot follow the theme.");
    }

    [Fact]
    public void ThereAreColoursToCheckAtAll()
    {
        // The rule is worth nothing if the walk finds nothing: every one of these
        // files does paint, and the allowed four are proof the reader works.
        Assert.True(s_screens.Sum(f => ColoursIn(Path.Combine(f)).Count) >= 4);
    }

    [Fact]
    public void TheBadgeOnAVideoTileDoesNotBorrowTheChipBrushes()
    {
        // The two badges shared one brush pair until the handoff gave them
        // different answers. The chip on a card stays on Badge.*; the one on a
        // photograph needs the scrim, and the failure mode of getting this wrong
        // is not an exception but a badge that vanishes on a pale picture.
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        foreach (string badge in BadgesIn(markup))
        {
            Assert.DoesNotContain("Badge.Background}", badge, StringComparison.Ordinal);
            Assert.DoesNotContain("{DynamicResource Badge.Foreground}", badge, StringComparison.Ordinal);
        }

        Assert.True(BadgesIn(markup).Count >= 4, "the grids that draw video badges have gone missing");
    }

    public static TheoryData<string> Screens
    {
        get
        {
            TheoryData<string> data = [];
            foreach (string[] segments in s_screens)
            {
                data.Add(Path.Combine(segments));
            }

            return data;
        }
    }

    /// <summary>Every literal colour written into a file's attributes.</summary>
    private static List<string> ColoursIn(string file)
    {
        XDocument document = XDocument.Load(AppMarkup.PathTo([.. file.Split(Path.DirectorySeparatorChar)]));

        return [.. document.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value.Trim())
            .Where(value => value.StartsWith('#')
                            || value.Equals("White", StringComparison.OrdinalIgnoreCase)
                            || value.Equals("Black", StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>The markup of each Border styled as a video badge.</summary>
    private static List<string> BadgesIn(string markup)
    {
        List<string> badges = [];
        const string opening = "<Border Style=\"{DynamicResource VideoBadge}\"";

        for (int at = markup.IndexOf(opening, StringComparison.Ordinal); at >= 0;
             at = markup.IndexOf(opening, at + 1, StringComparison.Ordinal))
        {
            int close = markup.IndexOf("</Border>", at, StringComparison.Ordinal);
            badges.Add(markup[at..close]);
        }

        return badges;
    }
}

using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// That the numbered neutral ramp is actually a ramp.
/// </summary>
/// <remarks>
/// Its whole promise is that Neutral.N means the same amount of prominence in
/// either theme, so a pairing with enough contrast at one number has it at that
/// number in the other. Two ways to break that quietly: repeat a value, which
/// makes two things told apart by their step identical; or let the ladder change
/// direction, which turns "one step further from the surface" into a step back
/// towards it. Neither throws, and neither is obvious on the theme you happen to
/// be looking at.
/// </remarks>
public sealed class NeutralRampTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void EveryStepIsADistinctColour(string file)
    {
        string[] ramp = [.. RampOf(file)];

        Assert.Equal(6, ramp.Length);
        Assert.Equal(6, ramp.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void ItMovesTheSameWayAllTheWayUp(string file)
    {
        double[] steps = [.. RampOf(file).Select(Luminance)];

        // Dark climbs and light descends; what matters is that neither turns
        // back on itself half way.
        bool ascending = steps[^1] > steps[0];

        for (int i = 1; i < steps.Length; i++)
        {
            bool moved = ascending ? steps[i] > steps[i - 1] : steps[i] < steps[i - 1];
            Assert.True(
                moved,
                $"{file}: Neutral.{i + 1} turns back on Neutral.{i} "
                + $"({steps[i]:F3} against {steps[i - 1]:F3})");
        }
    }

    [Fact]
    public void TheTwoThemesRunInOppositeDirections()
    {
        // Not a stylistic observation - it is the reason the ramp is numbered
        // rather than named. If both ran the same way, one of them would be
        // walking away from its own surface.
        double[] dark = [.. RampOf("Dark.xaml").Select(Luminance)];
        double[] light = [.. RampOf("Light.xaml").Select(Luminance)];

        Assert.True(dark[^1] > dark[0], "the dark ramp should get lighter as it climbs");
        Assert.True(light[^1] < light[0], "the light ramp should get darker as it climbs");
    }

    /// <summary>The six ramp colours, resolved through their token names.</summary>
    private static IEnumerable<string> RampOf(string file)
    {
        XDocument document = XDocument.Load(ThemeFile(file));

        Dictionary<string, string> colours = document.Descendants()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(
                element => (string)element.Attribute(Xaml + "Key")!,
                element => element.Value.Trim());

        for (int step = 1; step <= 6; step++)
        {
            XElement brush = document.Descendants()
                .Single(element => (string?)element.Attribute(Xaml + "Key") == $"Neutral.{step}");

            string named = ((string)brush.Attribute("Color")!)
                .Replace("{StaticResource", string.Empty, StringComparison.Ordinal)
                .Replace("}", string.Empty, StringComparison.Ordinal)
                .Trim();

            yield return colours.TryGetValue(named, out string? hex) ? hex : named;
        }
    }

    /// <summary>Rec. 709 relative luminance of a #AARRGGBB or #RRGGBB string.</summary>
    private static double Luminance(string hex)
    {
        string rgb = hex.TrimStart('#');
        rgb = rgb.Length == 8 ? rgb[2..] : rgb;

        double red = Convert.ToInt32(rgb[..2], 16) / 255d;
        double green = Convert.ToInt32(rgb[2..4], 16) / 255d;
        double blue = Convert.ToInt32(rgb[4..6], 16) / 255d;

        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static string ThemeFile(string file)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
               && !Directory.Exists(Path.Combine(directory.FullName, "src", "PhotoGallery.App")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "PhotoGallery.App", "Theme", file);
    }
}

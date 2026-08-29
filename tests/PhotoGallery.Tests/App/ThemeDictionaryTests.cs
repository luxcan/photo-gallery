using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// That the two palettes are interchangeable.
/// </summary>
/// <remarks>
/// Switching theme replaces one dictionary with the other, so a key defined in
/// only one of them does not fail - the lookup simply finds nothing and the
/// control keeps whatever WPF's default was. That looks like a screen someone
/// forgot to finish, and only in one mode, which is exactly the kind of thing
/// nobody notices until a user in the other mode reports it.
///
/// <para>Read as XML rather than loaded as resources: what is being checked is
/// that the two files agree, and that is a fact about the files.</para>
/// </remarks>
public sealed class ThemeDictionaryTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TheTwoPalettesDefineExactlyTheSameKeys()
    {
        HashSet<string> dark = KeysOf("Dark.xaml");
        HashSet<string> light = KeysOf("Light.xaml");

        Assert.False(dark.Count == 0, "no keys were found - the theme folder was not located");

        string onlyDark = string.Join(", ", dark.Except(light).Order());
        string onlyLight = string.Join(", ", light.Except(dark).Order());

        Assert.True(onlyDark.Length == 0, $"defined only in Dark.xaml: {onlyDark}");
        Assert.True(onlyLight.Length == 0, $"defined only in Light.xaml: {onlyLight}");
    }

    /// <summary>
    /// Every brush resolves to a colour that is actually defined.
    /// </summary>
    /// <remarks>
    /// The brushes name their colours through StaticResource, so a typo in one
    /// of those names is a XAML parse failure at start-up - the window dies
    /// before it draws and the only trace is a Windows crash record. Cheaper to
    /// catch here.
    /// </remarks>
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void EveryBrushNamesAColourThatExists(string file)
    {
        XDocument document = XDocument.Load(PathTo(file));

        HashSet<string> colours =
        [
            .. document.Descendants()
                .Where(element => element.Name.LocalName == "Color")
                .Select(element => (string?)element.Attribute(Xaml + "Key") ?? string.Empty),
        ];

        foreach (XElement brush in document.Descendants()
                     .Where(element => element.Name.LocalName == "SolidColorBrush"))
        {
            string key = (string?)brush.Attribute(Xaml + "Key") ?? "?";
            string colour = (string?)brush.Attribute("Color") ?? string.Empty;

            // Either a literal hex, or a reference that has to resolve.
            if (!colour.Contains("StaticResource", StringComparison.Ordinal))
            {
                continue;
            }

            string named = colour
                .Replace("{StaticResource", string.Empty, StringComparison.Ordinal)
                .Replace("}", string.Empty, StringComparison.Ordinal)
                .Trim();

            Assert.True(colours.Contains(named), $"{file}: {key} points at missing colour {named}");
        }
    }

    private static HashSet<string> KeysOf(string file) =>
    [
        .. XDocument.Load(PathTo(file))
            .Descendants()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .Where(key => !string.IsNullOrEmpty(key))
            .Select(key => key!),
    ];

    /// <summary>
    /// The theme folder, found by walking up to the repository root.
    /// </summary>
    /// <remarks>
    /// The dictionaries are compiled into the app assembly rather than copied
    /// beside the tests, so they are read from source.
    /// </remarks>
    private static string PathTo(string file) => AppMarkup.PathTo("Theme", file);
}

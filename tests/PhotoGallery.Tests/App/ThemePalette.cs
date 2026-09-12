using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// A theme file read as a table of key to colour, with the token indirection
/// already followed.
/// </summary>
/// <remarks>
/// Almost every brush in <c>Light.xaml</c> and <c>Dark.xaml</c> names a colour
/// rather than carrying one - <c>Color="{StaticResource TextPrimaryColor}"</c> -
/// which is the whole point of the two vocabularies, and is also what makes a
/// test that wants to know what a key actually resolves to unable to simply read
/// an attribute.
///
/// <para>Three tests had written that resolution out separately before this
/// existed. It is one walk now, so a change to how the files are shaped breaks
/// in one place rather than in three.</para>
/// </remarks>
internal static class ThemePalette
{
    private static readonly XNamespace s_x = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Every <c>Color</c> and <c>SolidColorBrush</c> in a theme file, keyed by
    /// its <c>x:Key</c> and valued as an upper-case <c>#AARRGGBB</c> string.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Of(string fileName)
    {
        XDocument document = XDocument.Load(AppMarkup.PathTo("Theme", fileName));

        Dictionary<string, string> colours = new(StringComparer.Ordinal);

        foreach (XElement element in document.Descendants()
                     .Where(e => e.Name.LocalName == "Color"
                                 && e.Attribute(s_x + "Key") is not null))
        {
            colours[(string)element.Attribute(s_x + "Key")!] = element.Value.Trim().ToUpperInvariant();
        }

        Dictionary<string, string> resolved = new(colours, StringComparer.Ordinal);

        foreach (XElement brush in document.Descendants()
                     .Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            string key = (string)brush.Attribute(s_x + "Key")!;
            string stated = ((string?)brush.Attribute("Color") ?? string.Empty).Trim();

            resolved[key] = stated.StartsWith('{')
                ? colours[TokenIn(stated)]
                : stated.ToUpperInvariant();
        }

        return resolved;
    }

    /// <summary>The colour key inside a <c>{StaticResource X}</c> reference.</summary>
    private static string TokenIn(string reference) =>
        reference
            .Replace("{StaticResource", string.Empty, StringComparison.Ordinal)
            .Replace("{DynamicResource", string.Empty, StringComparison.Ordinal)
            .Replace("}", string.Empty, StringComparison.Ordinal)
            .Trim();
}

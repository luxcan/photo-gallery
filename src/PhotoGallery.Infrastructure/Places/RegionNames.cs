using System.Collections.Frozen;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace PhotoGallery.Infrastructure.Places;

/// <summary>
/// Turns the gazetteer's region codes into names: "MY.06" is Pahang.
/// </summary>
/// <remarks>
/// A file rather than a table in source, unlike <see cref="CountryNames"/>, and
/// for the obvious reason - there are 3,865 of these against 246, and nobody can
/// write them out correctly from memory. GeoNames publishes them as
/// <c>admin1CodesASCII.txt</c>; trimmed to the code and the name it is 27 KB
/// compressed, so it costs about as much as a small icon.
///
/// <para>Read once and held, and loaded lazily: a library with no coordinates in
/// it never pays for it.</para>
///
/// <para>Not every country has one. Singapore and Hong Kong are city-states with
/// no first-level divisions to name, so a lookup there answers null and the
/// screens simply show nothing between the district and the country - which is
/// how an address for those places reads anyway.</para>
/// </remarks>
internal static class RegionNames
{
    private const string ResourceName = "PhotoGallery.Infrastructure.Places.admin1.br";

    private static readonly Lazy<FrozenDictionary<string, string>> s_names = new(Read);

    /// <summary>
    /// The name for a country and region code pair, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Takes the two halves separately because that is how the gazetteer stores
    /// them - the composite "MY.06" is this table's key, not the app's.
    /// </remarks>
    public static string? Of(string? countryCode, string? admin1Code)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(admin1Code))
        {
            return null;
        }

        return s_names.Value.TryGetValue($"{countryCode}.{admin1Code}", out string? name)
            ? name
            : null;
    }

    /// <summary>How many regions are known, for the test that checks the resource arrived.</summary>
    public static int Count => s_names.Value.Count;

    private static FrozenDictionary<string, string> Read()
    {
        Stream? compressed = typeof(RegionNames)
            .GetTypeInfo()
            .Assembly
            .GetManifestResourceStream(ResourceName);

        if (compressed is null)
        {
            // An absent resource leaves every region unnamed, which shows up as
            // regions simply not being offered - never as a wrong name.
            return FrozenDictionary<string, string>.Empty;
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using (var brotli = new BrotliStream(compressed, CompressionMode.Decompress))
        using (var text = new StreamReader(brotli))
        {
            while (text.ReadLine() is string line)
            {
                int tab = line.IndexOf('\t', StringComparison.Ordinal);
                if (tab > 0 && tab < line.Length - 1)
                {
                    names[line[..tab]] = line[(tab + 1)..];
                }
            }
        }

        return names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}

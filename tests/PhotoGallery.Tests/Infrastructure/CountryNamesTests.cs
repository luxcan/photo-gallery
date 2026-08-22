using System.IO.Compression;
using System.Reflection;
using PhotoGallery.Infrastructure.Places;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The table that turns "HK" into "Hong Kong".
/// </summary>
/// <remarks>
/// It is written out by hand, so the test that matters is not that a few entries
/// are right - it is that <b>none is missing</b>. A code in the data with no name
/// here would put a two-letter code in front of somebody, or drop a whole
/// country out of the search box, and nothing else in the suite would notice.
/// </remarks>
public sealed class CountryNamesTests
{
    [Fact]
    public void EveryCountryInTheGazetteerHasAName()
    {
        string[] missing =
        [
            .. GazetteerCountryCodes()
                .Where(code => CountryNames.Of(code) is null)
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            missing.Length == 0,
            $"the gazetteer uses {missing.Length} country code(s) with no name: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// And nothing here that the gazetteer never uses.
    /// </summary>
    /// <remarks>
    /// A name for a code that cannot occur is dead weight, and more to the point
    /// it means the table and the data have drifted - which is worth catching in
    /// the direction that is merely untidy, so that the direction that is a bug
    /// stays trustworthy.
    /// </remarks>
    [Fact]
    public void NoNameIsKeptForACountryTheGazetteerNeverUses()
    {
        HashSet<string> used = new(GazetteerCountryCodes(), StringComparer.OrdinalIgnoreCase);

        string[] spare = [.. CountryNames.Codes.Where(code => !used.Contains(code)).Order(StringComparer.Ordinal)];

        Assert.True(spare.Length == 0, $"named but never used: {string.Join(", ", spare)}");
    }

    [Theory]
    [InlineData("HK", "Hong Kong")]
    [InlineData("TW", "Taiwan")]
    [InlineData("SG", "Singapore")]
    [InlineData("MY", "Malaysia")]
    [InlineData("hk", "Hong Kong")]
    public void Of_NamesACode(string code, string expected) =>
        Assert.Equal(expected, CountryNames.Of(code));

    [Fact]
    public void Of_IsNothingForACodeItDoesNotKnow()
    {
        Assert.Null(CountryNames.Of("ZZ"));
        Assert.Null(CountryNames.Of(null));
        Assert.Null(CountryNames.Of(string.Empty));
    }

    /// <summary>
    /// The region table arrived and parses.
    /// </summary>
    /// <remarks>
    /// Unlike the countries, these come from an embedded file, so the failure to
    /// guard against is not a missing entry but a missing resource - which would
    /// leave every region silently unnamed and simply never offered.
    /// </remarks>
    [Fact]
    public void TheRegionTableIsPresentAndNamesRealPlaces()
    {
        Assert.InRange(RegionNames.Count, 3_000, 5_000);

        Assert.Equal("Pahang", RegionNames.Of("MY", "06"));
        Assert.Equal("Victoria", RegionNames.Of("AU", "07"));
        Assert.Equal("Tokyo", RegionNames.Of("JP", "40"));

        // Case is not the caller's problem.
        Assert.Equal("Pahang", RegionNames.Of("my", "06"));
    }

    /// <summary>
    /// A city-state has no region, and that is an answer rather than a gap.
    /// </summary>
    [Fact]
    public void Of_IsNothingWhereThereIsNoRegionToName()
    {
        Assert.Null(RegionNames.Of("SG", "01"));
        Assert.Null(RegionNames.Of("MY", null));
        Assert.Null(RegionNames.Of(null, "06"));
        Assert.Null(RegionNames.Of("ZZ", "99"));
    }

    /// <summary>
    /// Every country code the embedded gazetteer actually uses, read from the
    /// resource itself.
    /// </summary>
    /// <remarks>
    /// Read here rather than through a method on the gazetteer, so that checking
    /// the table does not add a member to the production type that only a test
    /// would ever call.
    /// </remarks>
    private static IEnumerable<string> GazetteerCountryCodes()
    {
        Stream? compressed = typeof(GeoNamesGazetteer)
            .GetTypeInfo()
            .Assembly
            .GetManifestResourceStream("PhotoGallery.Infrastructure.Places.cities500.br");

        Assert.NotNull(compressed);

        using var brotli = new BrotliStream(compressed!, CompressionMode.Decompress);
        using var text = new StreamReader(brotli);

        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        while (text.ReadLine() is string line)
        {
            string[] parts = line.Split('\t');
            if (parts.Length >= 6 && parts[4].Length > 0)
            {
                codes.Add(parts[4]);
            }
        }

        Assert.NotEmpty(codes);
        return codes;
    }
}

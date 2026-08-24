using System.Text;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Places;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Finding the nearest place to a coordinate, offline.
/// </summary>
/// <remarks>
/// The search is tested against a handful of rows; the data it really ships
/// with is tested once, at the bottom, because a resource that failed to embed
/// would leave every lookup returning null and every one of these passing.
/// </remarks>
public sealed class GeoNamesGazetteerTests
{
    [Fact]
    public void Resolve_FindsTheNearestPlace()
    {
        IGeocoder geocoder = Gazetteer(
            Row(1735161, "Kampung Bukit Tinggi, Bentong", 3.3494, 101.8263, "MY", "06"),
            Row(1735158, "Bentong Town", 3.5223, 101.9087, "MY", "06"),
            Row(1735006, "Kuala Lumpur", 3.1390, 101.6869, "MY", "14"));

        GazetteerPlace? found = geocoder.Resolve(3.4239, 101.7930);

        Assert.NotNull(found);
        Assert.Equal("Kampung Bukit Tinggi, Bentong", found!.Name);
        Assert.Equal("MY", found.CountryCode);
        Assert.Equal("06", found.Admin1Code);
        Assert.InRange(found.Kilometres, 8d, 11d);
    }

    /// <summary>
    /// The nearest place is not always in the same grid cell as the photograph.
    /// </summary>
    /// <remarks>
    /// The bug the neighbouring-cell sweep exists to prevent. A coordinate just
    /// inside one degree and a town just inside the next are a kilometre apart
    /// and in different buckets; a lookup that searched only its own cell would
    /// silently pick something forty times further away, or nothing.
    /// </remarks>
    [Fact]
    public void Resolve_LooksAcrossTheCellBoundary()
    {
        IGeocoder geocoder = Gazetteer(
            Row(1, "Just Over The Line", 4.001, 101.5, "MY", "01"),
            Row(2, "Far Away But Same Cell", 3.5, 101.5, "MY", "01"));

        // Sitting at 3.999, which is in cell 3 - the near town is in cell 4.
        GazetteerPlace? found = geocoder.Resolve(3.999, 101.5);

        Assert.NotNull(found);
        Assert.Equal("Just Over The Line", found!.Name);
    }

    [Fact]
    public void Resolve_RefusesToNameSomewhereTooFarAway()
    {
        // A photograph at sea. The nearest place is real and hundreds of
        // kilometres off, and naming it would be worse than saying nothing -
        // nothing on screen would say how far.
        IGeocoder geocoder = Gazetteer(
            Row(1, "Kuala Lumpur", 3.1390, 101.6869, "MY", "14"));

        Assert.Null(geocoder.Resolve(0.0, -30.0));
    }

    [Fact]
    public void Resolve_StillWorksWhereADegreeOfLongitudeIsShort()
    {
        // At 78 north a degree of longitude is about 23 km, so a fixed
        // three-by-three block of cells would be narrower than the search radius
        // and would miss this.
        IGeocoder geocoder = Gazetteer(
            Row(2729907, "Longyearbyen", 78.2232, 15.6267, "SJ", "00"));

        GazetteerPlace? found = geocoder.Resolve(78.2232, 15.6267);

        Assert.NotNull(found);
        Assert.Equal("Longyearbyen", found!.Name);
    }

    [Fact]
    public void Resolve_IsNothingForACoordinateThatCannotExist()
    {
        IGeocoder geocoder = Gazetteer(Row(1, "Somewhere", 3.1, 101.6, "MY", "14"));

        Assert.Null(geocoder.Resolve(91d, 101.6));
        Assert.Null(geocoder.Resolve(3.1, 181d));
        Assert.Null(geocoder.Resolve(double.NaN, 101.6));
    }

    [Fact]
    public void Resolve_SkipsRowsItCannotRead()
    {
        // One malformed line in a third of a million must not cost the whole
        // feature.
        IGeocoder geocoder = Gazetteer(
            "this is not a gazetteer row",
            "\t\t\t\t\t",
            Row(1735006, "Kuala Lumpur", 3.1390, 101.6869, "MY", "14"));

        Assert.Equal("Kuala Lumpur", geocoder.Resolve(3.1390, 101.6869)?.Name);
    }

    [Fact]
    public void Resolve_IsNothingWhenThereIsNoDataAtAll()
    {
        IGeocoder geocoder = new GeoNamesGazetteer(() => null);

        Assert.Null(geocoder.Resolve(3.4239, 101.7930));
    }

    /// <summary>
    /// The data the app actually ships with, exercised through the real
    /// constructor.
    /// </summary>
    /// <remarks>
    /// Everything above builds its own rows, so all of it would still pass if
    /// the embedded resource failed to compile in, was renamed, or arrived
    /// corrupt - and every photograph in the library would quietly go unplaced.
    /// This is the test that would fail instead.
    ///
    /// <para>The places asserted are far apart, on four continents and in both
    /// hemispheres, so a partly-read file cannot satisfy them. Distances are
    /// generous: what is being checked is that the gazetteer is present and
    /// broadly right, not GeoNames' own precision.</para>
    /// </remarks>
    [Theory]
    [InlineData(3.1390, 101.6869, "Kuala Lumpur", "MY")]
    [InlineData(51.5074, -0.1278, "London", "GB")]
    [InlineData(-37.8136, 144.9631, "Melbourne", "AU")]
    [InlineData(78.2232, 15.6267, "Longyearbyen", "SJ")]
    public void TheEmbeddedGazetteer_NamesSomewhereKnown(
        double latitude, double longitude, string expected, string country)
    {
        GazetteerPlace? found = new GeoNamesGazetteer().Resolve(latitude, longitude);

        Assert.NotNull(found);
        Assert.Equal(expected, found!.Name);
        Assert.Equal(country, found.CountryCode);
        Assert.InRange(found.Kilometres, 0d, 5d);
    }

    [Fact]
    public void TheEmbeddedGazetteer_LeavesTheOpenOceanUnnamed()
    {
        // 30 km from nothing at all, in the real data rather than a fixture.
        Assert.Null(new GeoNamesGazetteer().Resolve(0.0, -30.0));
    }

    /// <summary>One row in the trimmed six-column layout the app embeds.</summary>
    private static string Row(
        int id, string name, double latitude, double longitude, string country, string admin1) =>
        string.Join(
            '\t',
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            name,
            latitude.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            longitude.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            country,
            admin1);

    private static IGeocoder Gazetteer(params string[] rows) =>
        new GeoNamesGazetteer(
            () => new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', rows))));
}

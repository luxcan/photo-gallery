using PhotoGallery.Infrastructure.Imaging;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Turning what EXIF holds into a pair of degrees.
/// </summary>
/// <remarks>
/// Worth pinning because every failure here is silent and confident. A latitude
/// read as a plain integer, or a southern one read without its reference, still
/// produces a perfectly valid coordinate - it simply belongs to somewhere else,
/// and the app then names that somewhere and shows it to the user.
/// </remarks>
public sealed class GpsCoordinatesTests
{
    /// <summary>
    /// One EXIF rational, packed the way WIC hands it back.
    /// </summary>
    /// <remarks>
    /// Numerator in the low 32 bits, denominator in the high 32. This helper is
    /// the test's own statement of that layout: if the reader ever disagreed
    /// with it, every assertion below would move at once.
    /// </remarks>
    private static ulong Rational(uint numerator, uint denominator) =>
        ((ulong)denominator << 32) | numerator;

    /// <summary>3° 25' 26.04" — Genting, and the PRP's own worked example.</summary>
    private static ulong[] Genting => [Rational(3, 1), Rational(25, 1), Rational(2604, 100)];

    /// <summary>101° 47' 34.8".</summary>
    private static ulong[] GentingEast => [Rational(101, 1), Rational(47, 1), Rational(348, 10)];

    [Fact]
    public void From_FoldsDegreesMinutesAndSecondsIntoOneNumber()
    {
        (double Latitude, double Longitude)? where =
            GpsCoordinates.From(Genting, "N", GentingEast, "E");

        Assert.NotNull(where);
        Assert.Equal(3.4239, where!.Value.Latitude, 4);
        Assert.Equal(101.7930, where.Value.Longitude, 4);
    }

    [Fact]
    public void From_ReadsTheHemisphereRatherThanAssumingTheNorthEast()
    {
        // Melbourne and Mongolia can carry the same latitude and differ only by
        // this letter. Ignoring it silently moves half the world.
        (double Latitude, double Longitude)? south =
            GpsCoordinates.From(Genting, "S", GentingEast, "W");

        Assert.NotNull(south);
        Assert.Equal(-3.4239, south!.Value.Latitude, 4);
        Assert.Equal(-101.7930, south.Value.Longitude, 4);
    }

    [Fact]
    public void From_TrimsTheTerminatorExifPadsItsStringsWith()
    {
        // "S\0" is not "S", and a comparison that missed this would put every
        // southern photograph in the north while looking perfectly correct.
        (double Latitude, double Longitude)? where =
            GpsCoordinates.From(Genting, "S\0", GentingEast, "E\0");

        Assert.NotNull(where);
        Assert.True(where!.Value.Latitude < 0);
        Assert.True(where.Value.Longitude > 0);
    }

    [Fact]
    public void From_IsNothingWhenEitherHalfIsMissing()
    {
        // Half a coordinate is not a coordinate. Defaulting the other half to
        // zero would place the photograph on the equator or the meridian.
        Assert.Null(GpsCoordinates.From(null, "N", GentingEast, "E"));
        Assert.Null(GpsCoordinates.From(Genting, "N", null, "E"));
        Assert.Null(GpsCoordinates.From(null, null, null, null));
    }

    [Fact]
    public void From_TreatsAMissingReferenceAsThePositiveHemisphere()
    {
        // N and E are the defaults EXIF omits. Refusing the whole coordinate
        // over an absent reference would lose photographs that are perfectly
        // well placed.
        (double Latitude, double Longitude)? where =
            GpsCoordinates.From(Genting, null, GentingEast, null);

        Assert.NotNull(where);
        Assert.Equal(3.4239, where!.Value.Latitude, 4);
    }

    [Theory]
    [InlineData(91)]
    [InlineData(180)]
    public void From_RefusesALatitudeThatCannotExist(uint degrees)
    {
        ulong[] impossible = [Rational(degrees, 1), Rational(0, 1), Rational(0, 1)];

        Assert.Null(GpsCoordinates.From(impossible, "N", GentingEast, "E"));
    }

    [Fact]
    public void From_RefusesALongitudePastTheAntimeridian()
    {
        ulong[] impossible = [Rational(181, 1), Rational(0, 1), Rational(0, 1)];

        Assert.Null(GpsCoordinates.From(Genting, "N", impossible, "E"));
    }

    /// <summary>
    /// Null Island, which is a sentinel rather than a place anyone has been.
    /// </summary>
    /// <remarks>
    /// Several cameras write exactly zero when the receiver never got a fix. It
    /// is a valid coordinate in the Gulf of Guinea, so nothing about the number
    /// gives it away - and taken at face value it puts a run of photographs in
    /// the Atlantic and then names them.
    /// </remarks>
    [Fact]
    public void From_RefusesExactlyZeroZero()
    {
        ulong[] zero = [Rational(0, 1), Rational(0, 1), Rational(0, 1)];

        Assert.Null(GpsCoordinates.From(zero, "N", zero, "E"));
    }

    [Fact]
    public void From_KeepsAZeroThatIsOnlyHalfOfThePair()
    {
        // On the equator but not on the meridian. Only both being zero is the
        // sentinel; one of them is an ordinary coordinate.
        ulong[] zero = [Rational(0, 1), Rational(0, 1), Rational(0, 1)];

        Assert.NotNull(GpsCoordinates.From(zero, "N", GentingEast, "E"));
    }

    [Fact]
    public void From_SurvivesTheZeroDenominatorSomeEncodersWrite()
    {
        ulong[] unused = [Rational(3, 1), Rational(25, 1), Rational(26, 0)];

        (double Latitude, double Longitude)? where =
            GpsCoordinates.From(unused, "N", GentingEast, "E");

        // The seconds are dropped rather than the coordinate, and nothing
        // divides by zero on the way.
        Assert.NotNull(where);
        Assert.Equal(3 + (25d / 60d), where!.Value.Latitude, 6);
    }

    [Fact]
    public void From_AcceptsACodecThatHandsBackPlainNumbers()
    {
        // Nothing requires a codec to give back the packed form, and being
        // strict about the container would gain nothing.
        double[] degrees = [3d, 25d, 26.04d];
        double[] east = [101d, 47d, 34.8d];

        (double Latitude, double Longitude)? where =
            GpsCoordinates.From(degrees, "N", east, "E");

        Assert.NotNull(where);
        Assert.Equal(3.4239, where!.Value.Latitude, 4);
    }

    [Fact]
    public void From_IsNothingWhenTheTagIsSomethingElseEntirely()
    {
        Assert.Null(GpsCoordinates.From("3.4239", "N", "101.7930", "E"));
    }
}

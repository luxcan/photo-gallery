namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Turns what EXIF holds into a pair of signed decimal degrees, or nothing.
/// </summary>
/// <remarks>
/// Separate from the generator that reads the tags, and free of any imaging
/// type, because all the ways this goes wrong are arithmetic rather than
/// decoding - and a wrong coordinate is worse than no coordinate. A missing one
/// leaves a photograph unplaced, which is already true of 61% of this library.
/// A wrong one gets confidently named, and the name is what the user sees.
///
/// <para>Everything here refuses rather than guesses. There is no such thing as
/// a partial coordinate worth keeping.</para>
/// </remarks>
public static class GpsCoordinates
{
    /// <summary>
    /// The signed pair, or null when the tags are absent, malformed or not
    /// credible.
    /// </summary>
    public static (double Latitude, double Longitude)? From(
        object? latitude, object? latitudeRef, object? longitude, object? longitudeRef)
    {
        if (ToDegrees(latitude) is not double lat || ToDegrees(longitude) is not double lon)
        {
            return null;
        }

        // South and West are the negative halves. EXIF stores the magnitude
        // only, so without these every southern photograph lands in the north.
        if (IsNegative(latitudeRef, 'S'))
        {
            lat = -lat;
        }

        if (IsNegative(longitudeRef, 'W'))
        {
            lon = -lon;
        }

        if (double.IsNaN(lat) || double.IsNaN(lon)
            || Math.Abs(lat) > 90d || Math.Abs(lon) > 180d)
        {
            return null;
        }

        // Null Island. Several cameras and phones write exactly zero when the
        // receiver never got a fix, and it is indistinguishable from a real
        // reading except that nobody in this library has been to the Gulf of
        // Guinea. Taking it at face value would put a run of photographs in the
        // Atlantic and name them.
        if (lat == 0d && lon == 0d)
        {
            return null;
        }

        return (lat, lon);
    }

    /// <summary>Degrees, minutes and seconds folded into one number.</summary>
    private static double? ToDegrees(object? value)
    {
        if (Rationals(value) is not double[] parts || parts.Length == 0)
        {
            return null;
        }

        double degrees = parts[0];

        if (parts.Length > 1)
        {
            degrees += parts[1] / 60d;
        }

        if (parts.Length > 2)
        {
            degrees += parts[2] / 3600d;
        }

        return degrees;
    }

    /// <summary>
    /// The three rationals EXIF holds a coordinate as.
    /// </summary>
    /// <remarks>
    /// WIC hands back an EXIF rational as a <c>ulong</c> with the numerator in
    /// the low 32 bits and the denominator in the high 32 - not as a number.
    /// Read as a plain integer, a latitude of 3° 25' 26" comes out as roughly
    /// 4.3 billion. The other shapes are accepted because a codec is free to
    /// normalise, and being strict about the container gains nothing.
    /// </remarks>
    private static double[]? Rationals(object? value) => value switch
    {
        ulong[] packed => [.. packed.Select(Unpack)],
        long[] packed => [.. packed.Select(part => Unpack(unchecked((ulong)part)))],
        double[] parts => parts,
        float[] parts => [.. parts.Select(part => (double)part)],
        ulong single => [Unpack(single)],
        double single => [single],
        _ => null,
    };

    private static double Unpack(ulong rational)
    {
        uint numerator = unchecked((uint)(rational & 0xFFFFFFFFul));
        uint denominator = unchecked((uint)(rational >> 32));

        // A zero denominator is how some encoders spell "this field is unused".
        return denominator == 0u ? 0d : numerator / (double)denominator;
    }

    /// <summary>
    /// Whether the reference names the negative hemisphere.
    /// </summary>
    /// <remarks>
    /// Trimmed of its terminator before comparing: EXIF strings are
    /// null-padded, and "S\0" does not equal "S".
    /// </remarks>
    private static bool IsNegative(object? reference, char negative)
    {
        if (reference is not string text)
        {
            return false;
        }

        ReadOnlySpan<char> trimmed = text.AsSpan().Trim('\0').Trim();

        return trimmed.Length > 0
            && char.ToUpperInvariant(trimmed[0]) == negative;
    }
}

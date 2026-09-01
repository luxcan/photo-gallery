namespace PhotoGallery.Domain.Places;

/// <summary>
/// Distance between two points on the earth, in kilometres.
/// </summary>
/// <remarks>
/// Lifted out of the gazetteer, which had it private, when a second caller
/// appeared: albums need to know whether a group of photographs sits far
/// enough from home to be called a trip. Two copies of a distance function is
/// how two answers to one question start.
/// </remarks>
public static class Coordinates
{
    /// <summary>
    /// Degrees of latitude to kilometres. Longitude narrows towards the poles,
    /// which is what the cosine below corrects for.
    /// </summary>
    public const double KilometresPerDegree = 111.32d;

    /// <summary>
    /// Straight-line distance, flat-earth style.
    /// </summary>
    /// <remarks>
    /// Equirectangular rather than haversine. Over the tens of kilometres this
    /// ever compares, the error against the true great-circle distance is
    /// centimetres - and it costs one cosine instead of four trigonometric
    /// calls, of which the gazetteer makes hundreds per photograph.
    /// </remarks>
    public static double Kilometres(
        double latitude, double longitude, double otherLatitude, double otherLongitude)
    {
        double meanLatitude = (latitude + otherLatitude) / 2d * Math.PI / 180d;
        double x = (longitude - otherLongitude) * Math.Cos(meanLatitude);
        double y = latitude - otherLatitude;

        return Math.Sqrt((x * x) + (y * y)) * KilometresPerDegree;
    }
}

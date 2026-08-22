namespace PhotoGallery.Application.Ports;

/// <summary>
/// Turns a coordinate into the name of a place, without a network.
/// </summary>
/// <remarks>
/// Offline by design rather than by circumstance. This is where a family has
/// been for twelve years, and looking each one up would send that history to
/// somebody else's server a coordinate at a time - for an answer no better than
/// a local file gives, since a collection needs the district and not the street.
/// </remarks>
public interface IGeocoder
{
    /// <summary>
    /// The nearest known place, or null when there is none close enough to be
    /// worth the name.
    /// </summary>
    /// <remarks>
    /// Null is a real answer and a common one. A photograph taken at sea, in a
    /// desert or halfway up a mountain has no populated place near it, and the
    /// nearest one may be a hundred kilometres away - naming it would be worse
    /// than saying nothing, because the user cannot see how far off it is.
    /// </remarks>
    GazetteerPlace? Resolve(double latitude, double longitude);
}

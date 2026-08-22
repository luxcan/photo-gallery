namespace PhotoGallery.Application.Ports;

/// <summary>
/// A settled answer about where one photograph was taken.
/// </summary>
/// <remarks>
/// Every field but the marker is nullable because "nothing" is a real answer
/// here, and the three shapes are all legitimate: coordinates and a place, for a
/// photograph taken near a town the gazetteer knows; coordinates and no place,
/// for one taken more than thirty kilometres from anywhere populated; and
/// neither, for a camera with no receiver. Only a photograph that could not be
/// read produces no <see cref="PhotoLocation"/> at all.
/// </remarks>
/// <param name="ReadUtc">
/// When the question was settled. Written whatever the answer was, because it is
/// the record of having asked rather than of having found something.
/// </param>
public readonly record struct PhotoLocation(
    int AssetId,
    double? Latitude,
    double? Longitude,
    int? PlaceId,
    DateTime ReadUtc);

namespace PhotoGallery.Application.Ports;

/// <summary>One place from the gazetteer, and how far it was from the photograph.</summary>
/// <param name="Kilometres">
/// The distance from the coordinate that was asked about. Carried so a caller
/// can say "near" rather than "at", and so the decision to accept a match at all
/// is visible rather than buried in the lookup.
/// </param>
public sealed record GazetteerPlace(
    int GeoNameId,
    string Name,
    string? CountryCode,
    string? Admin1Code,
    double Latitude,
    double Longitude,
    double Kilometres);

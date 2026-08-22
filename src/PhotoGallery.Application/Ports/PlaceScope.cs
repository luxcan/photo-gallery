namespace PhotoGallery.Application.Ports;

/// <summary>
/// How wide a place filter is.
/// </summary>
/// <remarks>
/// Two scopes because the gazetteer's own answer is not the one people search
/// with. It names populated places, so in a dense city the nearest is a district:
/// Hong Kong photographs come back as Tsim Sha Tsui, Central and Mid Levels, and
/// nobody types those to find their holiday. The country is the scope the trip
/// had a name at.
///
/// <para>Not always inconsistent in the same direction, which is why both are
/// needed rather than one: Taipei and Busan resolve to the city, because nothing
/// smaller sits nearer.</para>
/// </remarks>
public enum PlaceScope
{
    /// <summary>One gazetteer place - a town, or a district of a city.</summary>
    Place,

    /// <summary>Every place in one first-level division: a state, province or county.</summary>
    /// <remarks>
    /// The rung between the two, and the one people use for a country large
    /// enough that naming it says little - "Victoria" rather than "Australia",
    /// "Pahang" rather than "Malaysia". City-states have none, and there the
    /// district and the country are the whole of the address.
    /// </remarks>
    Region,

    /// <summary>Every place in one country.</summary>
    Country,
}

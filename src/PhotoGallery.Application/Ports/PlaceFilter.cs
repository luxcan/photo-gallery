namespace PhotoGallery.Application.Ports;

/// <summary>
/// Which photographs a place restriction admits.
/// </summary>
/// <remarks>
/// One value rather than a pair of mutually exclusive nullable fields on every
/// query, view-model and port it passes through. The two scopes are answered by
/// different columns - one is the asset's own place, the other is a property of
/// the place it points at - and keeping them in one closed value means a caller
/// cannot set both, or neither, or forget that the second exists.
/// </remarks>
public readonly record struct PlaceFilter
{
    /// <summary>Which kind of restriction this is.</summary>
    public PlaceScope Scope { get; private init; }

    /// <summary>The place, meaningful only when <see cref="Scope"/> is Place.</summary>
    public int PlaceId { get; private init; }

    /// <summary>The country. Set for both Country and Region.</summary>
    /// <remarks>
    /// A region code is only unique within its country - "06" is Pahang in
    /// Malaysia and something else everywhere else - so a region filter carries
    /// both halves or it is not a filter at all.
    /// </remarks>
    public string? CountryCode { get; private init; }

    /// <summary>The first-level division, meaningful only when <see cref="Scope"/> is Region.</summary>
    public string? Admin1Code { get; private init; }

    /// <summary>One gazetteer place exactly.</summary>
    public static PlaceFilter Exactly(int placeId) =>
        new() { Scope = PlaceScope.Place, PlaceId = placeId };

    /// <summary>Every place in one state, province or county.</summary>
    public static PlaceFilter InRegion(string countryCode, string admin1Code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(admin1Code);

        return new PlaceFilter
        {
            Scope = PlaceScope.Region,
            CountryCode = countryCode,
            Admin1Code = admin1Code,
        };
    }

    /// <summary>Every place in one country.</summary>
    public static PlaceFilter InCountry(string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        return new PlaceFilter { Scope = PlaceScope.Country, CountryCode = countryCode };
    }
}

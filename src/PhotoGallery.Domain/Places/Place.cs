namespace PhotoGallery.Domain.Places;

/// <summary>
/// A populated place, as the gazetteer describes it.
/// </summary>
/// <remarks>
/// Rows here come from the gazetteer rather than from the photographs, so one
/// place is one row however many pictures resolve to it - which is what
/// <see cref="GeoNameId"/> is for. Without a natural key, a second run that
/// re-resolved the same coordinates would insert a second Kuala Lumpur, and the
/// duplicate would be invisible until somebody counted.
///
/// <para><see cref="CountryCode"/> and <see cref="Admin1Code"/> hold the
/// gazetteer's raw codes - "MY", "07" - and not readable names, because the
/// file that names them is a separate download. They are stored rather than
/// discarded so that installing those tables later can render "Pahang,
/// Malaysia" without re-resolving a single photograph. Until then
/// <see cref="Name"/> is what is shown, and on this library it is already the
/// recognisable part: "Genting Highlands" needs no qualifying.</para>
/// </remarks>
public sealed class Place
{
    public int Id { get; set; }

    /// <summary>The gazetteer's own identifier, and this row's natural key.</summary>
    public int GeoNameId { get; set; }

    /// <summary>The place as a person would say it: "Genting Highlands".</summary>
    public required string Name { get; set; }

    /// <summary>ISO 3166-1 alpha-2, e.g. "MY". A code, not a name.</summary>
    public string? CountryCode { get; set; }

    /// <summary>The gazetteer's first-level division code, e.g. "07". A code, not a name.</summary>
    public string? Admin1Code { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }
}

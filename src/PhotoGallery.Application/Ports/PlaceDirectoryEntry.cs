namespace PhotoGallery.Application.Ports;

/// <summary>
/// Somewhere that can be searched for, and how many photographs it would return.
/// </summary>
/// <remarks>
/// A filter, its name, and its count - which is all the search box needs, at
/// either scope. A country entry and a district entry are the same shape here on
/// purpose: the box matches them the same way, offers them in the same list, and
/// hands whichever was chosen straight to the query.
///
/// <para>Only places some photograph actually resolved to. The gazetteer holds
/// 235,403 of them across 246 countries, and offering a search box the whole
/// world would be offering mostly places the user has never been.</para>
/// </remarks>
public sealed record PlaceDirectoryEntry(PlaceFilter Filter, string Name, int Photos)
{
    /// <summary>Whether this is a whole country rather than one place in it.</summary>
    public bool IsCountry => Filter.Scope == PlaceScope.Country;
}

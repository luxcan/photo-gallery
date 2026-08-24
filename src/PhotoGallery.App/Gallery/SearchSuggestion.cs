using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Gallery;

/// <summary>What kind of thing a suggestion in the search box is.</summary>
public enum SearchSuggestionKind
{
    Person,
    Place,
    Region,
    Country,
}

/// <summary>
/// One row the search box offers, and how many photographs choosing it would
/// show.
/// </summary>
/// <remarks>
/// One type for all three so the box can offer them in a single list. Separate
/// lists would mean the user reading a heading to find out what they are looking
/// at, and would make "the first suggestion" - which is what Enter falls back to
/// - ambiguous.
///
/// <para>Lives in the app rather than the application layer because it exists
/// for the dropdown. The directories it is built from are separate ports and
/// should stay that way: nothing outside this screen wants people and places in
/// one bag.</para>
/// </remarks>
/// <param name="Kind">
/// Shown on the row. Without it "Singapore" gives no clue whether it was matched
/// as somebody's name, as a district, or as the whole country - and those are
/// three different answers with three different counts.
/// </param>
public sealed record SearchSuggestion(
    SearchSuggestionKind Kind, int PersonId, PlaceFilter? Place, string DisplayName, int Photos)
{
    public static SearchSuggestion ForPerson(PersonDirectoryEntry person)
    {
        ArgumentNullException.ThrowIfNull(person);

        return new SearchSuggestion(
            SearchSuggestionKind.Person, person.Id, null, person.DisplayName, person.Photos);
    }

    public static SearchSuggestion ForPlace(PlaceDirectoryEntry place)
    {
        ArgumentNullException.ThrowIfNull(place);

        return new SearchSuggestion(
            place.Filter.Scope switch
            {
                PlaceScope.Country => SearchSuggestionKind.Country,
                PlaceScope.Region => SearchSuggestionKind.Region,
                _ => SearchSuggestionKind.Place,
            },
            0,
            place.Filter,
            place.Name,
            place.Photos);
    }

    /// <summary>The word shown beside the name to say which it is.</summary>
    public string KindLabel => Kind switch
    {
        SearchSuggestionKind.Person => "person",
        SearchSuggestionKind.Country => "country",
        SearchSuggestionKind.Region => "region",
        _ => "place",
    };
}

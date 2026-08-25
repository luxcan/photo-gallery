using PhotoGallery.Domain.Places;

namespace PhotoGallery.Domain.Collections;

/// <summary>
/// One place a collection's rule admits.
/// </summary>
/// <remarks>
/// Unlike the people, several places are an OR between themselves - a
/// photograph is taken in one place and cannot be in two - and the set of them
/// is then ANDed with everything else. "Anywhere in this list, with these people,
/// between these dates" is the only reading that can ever match anything.
/// </remarks>
public sealed class CollectionRulePlace
{
    public int CollectionId { get; set; }

    public Collection? Collection { get; set; }

    public int PlaceId { get; set; }

    public Place? Place { get; set; }
}

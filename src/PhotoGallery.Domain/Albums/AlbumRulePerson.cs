using PhotoGallery.Domain.People;

namespace PhotoGallery.Domain.Albums;

/// <summary>
/// One person an album's rule asks for.
/// </summary>
/// <remarks>
/// A row per person rather than a list in a column, because the rule is an AND:
/// a photograph has to hold every one of them, and that is a join rather than a
/// string to parse.
/// </remarks>
public sealed class AlbumRulePerson
{
    public int AlbumId { get; set; }

    public Album? Album { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }
}

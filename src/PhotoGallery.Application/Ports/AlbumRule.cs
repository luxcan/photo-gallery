namespace PhotoGallery.Application.Ports;

/// <summary>
/// What an album is looking for: dates, people, places.
/// </summary>
/// <remarks>
/// The three parts are ANDed and each part is an OR within itself. A photograph
/// fits when it was taken inside the dates <em>and</em> holds one of the people
/// named <em>and</em> was taken in one of the places - so "Ana or Ben, in
/// Genting, that March" narrows to the occasion while still gathering everyone
/// who was there.
///
/// <para>The people were an AND to begin with, reasoning that a photograph can
/// hold two people at once where it cannot have been taken in two places at
/// once. True, and still the wrong rule: asking for all of them wants the
/// photographs where everybody happens to stand together, which past two names
/// is almost none - a three-name album that found one photograph is what sent
/// this back. The dates and the place are what narrow an album; the names say
/// who it is about.</para>
/// </remarks>
/// <param name="From">
/// The first day a photograph may have been taken on, or null for no limit.
/// A date rather than an instant: nobody thinks of an occasion as starting at
/// 09:14.
/// </param>
/// <param name="To">The last day, inclusive, or null for no limit.</param>
public sealed record AlbumRule(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<int> PersonIds,
    IReadOnlyList<int> PlaceIds)
{
    public static AlbumRule None { get; } = new(null, null, [], []);

    /// <summary>Whether there is anything here to look for.</summary>
    public bool IsSomething =>
        From is not null || To is not null || PersonIds.Count > 0 || PlaceIds.Count > 0;

    /// <summary>
    /// Two rules are the same when they ask for the same things.
    /// </summary>
    /// <remarks>
    /// Written out because a record with list members compares those members by
    /// reference, so a rule read back from the database would never equal the
    /// one just written - and the screen asks exactly that question to decide
    /// whether there is anything to save.
    /// </remarks>
    public bool Equals(AlbumRule? other) =>
        other is not null
        && From == other.From
        && To == other.To
        && PersonIds.SequenceEqual(other.PersonIds)
        && PlaceIds.SequenceEqual(other.PlaceIds);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(From);
        hash.Add(To);

        foreach (int personId in PersonIds)
        {
            hash.Add(personId);
        }

        foreach (int placeId in PlaceIds)
        {
            hash.Add(placeId);
        }

        return hash.ToHashCode();
    }
}

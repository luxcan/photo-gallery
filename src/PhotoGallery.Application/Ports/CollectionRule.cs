namespace PhotoGallery.Application.Ports;

/// <summary>
/// What a collection is looking for: dates, people, places.
/// </summary>
/// <remarks>
/// Everything here is ANDed. A photograph fits when it was taken inside the
/// dates <em>and</em> holds every person named <em>and</em> was taken in one of
/// the places - which is what makes the rule worth having: each part narrows
/// what the last one left, so "Ana and Ben, in Genting, that March" finds a
/// handful rather than a thousand.
///
/// <para>The people are an AND among themselves and the places an OR, and that
/// asymmetry is not a slip: a photograph can hold two people at once and cannot
/// have been taken in two places at once, so the other reading of either would
/// match nothing.</para>
/// </remarks>
/// <param name="From">
/// The first day a photograph may have been taken on, or null for no limit.
/// A date rather than an instant: nobody thinks of an occasion as starting at
/// 09:14.
/// </param>
/// <param name="To">The last day, inclusive, or null for no limit.</param>
public sealed record CollectionRule(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<int> PersonIds,
    IReadOnlyList<int> PlaceIds)
{
    public static CollectionRule None { get; } = new(null, null, [], []);

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
    public bool Equals(CollectionRule? other) =>
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

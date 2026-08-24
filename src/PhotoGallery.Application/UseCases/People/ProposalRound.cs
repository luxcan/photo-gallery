namespace PhotoGallery.Application.UseCases.People;

/// <summary>How many faces one person was given, before and after the cap.</summary>
public readonly record struct PersonProposals(int Offered, int Matched);

/// <summary>What one sweep of the library offered, and to whom.</summary>
/// <param name="Offered">How many proposals were written, after the per-person cap.</param>
/// <param name="Matched">
/// How many faces were routed to somebody at all. More than <paramref name="Offered"/>
/// means a cap was met, which is worth saying rather than hiding behind a round
/// number.
/// </param>
/// <param name="Considered">
/// How many unnamed faces were weighed, so a round that offers nothing can say
/// whether it looked.
/// </param>
/// <param name="ByPerson">What each named person was given.</param>
public sealed record ProposalRound(
    int Offered,
    int Matched,
    int Considered,
    IReadOnlyDictionary<int, PersonProposals> ByPerson)
{
    public PersonProposals For(int personId) =>
        ByPerson.TryGetValue(personId, out PersonProposals theirs) ? theirs : default;
}

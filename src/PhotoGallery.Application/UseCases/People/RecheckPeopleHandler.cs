using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>
/// Takes everything the user has said about who is who and applies it to the
/// whole library again.
/// </summary>
/// <remarks>
/// The user names a handful of faces in pictures they recognise; this is the
/// button that spreads that out. Every person's eras are rebuilt from their
/// confirmed examples, and then every unnamed face is weighed against all of
/// them at once - so one more example anywhere improves every answer everywhere.
///
/// <para>Eras first, everybody, before a single face is offered. That order is
/// the point: routing a face to the person it most resembles is only meaningful
/// once every person is up to date, and rebuilding one person and proposing in
/// the same breath would let whoever was rebuilt first answer with stale
/// examples.</para>
///
/// <para>It changes no confirmation and no rejection - only the proposals, which
/// were a question rather than a record.</para>
/// </remarks>
public sealed class RecheckPeopleHandler
{
    private readonly IPeopleReader _people;
    private readonly AssignFacesHandler _assign;
    private readonly ProposeFacesHandler _propose;

    public RecheckPeopleHandler(
        IPeopleReader people, AssignFacesHandler assign, ProposeFacesHandler propose)
    {
        _people = people;
        _assign = assign;
        _propose = propose;
    }

    public async Task<RecheckResult> HandleAsync(
        IProgress<RecheckProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Person> people =
            await _people.GetPeopleAsync(cancellationToken).ConfigureAwait(false);

        int done = 0;

        foreach (Person person in people)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new RecheckProgress(done, people.Count, person.DisplayName, 0));

            await _assign.RebuildErasAsync(person.Id, cancellationToken).ConfigureAwait(false);
            done++;
        }

        ProposalRound round = await _propose.HandleAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(new RecheckProgress(done, people.Count, string.Empty, round.Offered));
        return new RecheckResult(people.Count, round.Offered);
    }
}

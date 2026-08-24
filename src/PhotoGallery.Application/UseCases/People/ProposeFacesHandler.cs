using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>
/// Offers every unnamed face to whichever named person it most looks like.
/// </summary>
/// <remarks>
/// One pass over the library for everybody, rather than a pass per person. That
/// is not an optimisation - it is the only way the question can be answered
/// correctly. Asking "does this face look like Ana Lim?" in isolation cannot see
/// that it looks a great deal more like her brother, and a rule that lets the
/// first person over the line take the face makes the answer depend on the order
/// people are read in.
///
/// <para>Every proposal is withdrawn and remade, because a routing decision is
/// about the whole field: one more person named, or one more example given, can
/// change who a face should have gone to. Confirmations and rejections are never
/// touched - those are the user's, and this only ever revises the app's own
/// guesses.</para>
/// </remarks>
public sealed class ProposeFacesHandler
{
    /// <summary>How many proposals one person is offered at once.</summary>
    /// <remarks>
    /// The strongest first. More than this in one screen is not a review, it is
    /// a wall - and the next round offers the rest.
    /// </remarks>
    public const int MaxProposals = 300;

    /// <summary>
    /// How sure the detector must have been before a face is offered as anyone.
    /// </summary>
    /// <remarks>
    /// The detector keeps anything above 0.5, and the bottom of that range is
    /// where it puts boxes on things that are not faces at all. Measured on this
    /// library: 681 faces sit between 0.5 and 0.6, while the lowest score among
    /// every face the user has actually confirmed is 0.62. So nothing below this
    /// has ever turned out to be someone, and offering it only spends the user's
    /// attention on the detector's mistakes.
    ///
    /// <para>They are still recorded and still shown on the photograph - this
    /// decides what is worth asking about, not what was found.</para>
    /// </remarks>
    public const float MinimumProposalScore = 0.6f;

    private readonly IPeopleReader _people;
    private readonly IPeopleRepository _repository;

    public ProposeFacesHandler(IPeopleReader people, IPeopleRepository repository)
    {
        _people = people;
        _repository = repository;
    }

    public async Task<ProposalRound> HandleAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Person> everyone =
            await _people.GetPeopleAsync(cancellationToken).ConfigureAwait(false);

        // Withdrawn before anything is read back, so that a face proposed by the
        // last round counts as unclaimed in this one. Reading first would make
        // every already-proposed face invisible and the round a no-op.
        foreach (Person person in everyone)
        {
            await _repository.ClearProposalsAsync(person.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        List<Person> candidates = [.. everyone.Where(person => person.Eras.Count > 0)];
        if (candidates.Count == 0)
        {
            return new ProposalRound(0, 0, 0, new Dictionary<int, PersonProposals>());
        }

        IReadOnlyList<FaceRecord> faces = await _people
            .GetFacesAsync(includeEmbeddings: true, cancellationToken)
            .ConfigureAwait(false);

        HashSet<(int FaceId, int PersonId)> refused =
        [
            .. (await _people.GetRejectionsAsync(cancellationToken).ConfigureAwait(false))
                .Select(rejection => (rejection.FaceId, rejection.PersonId)),
        ];

        Dictionary<int, List<ScoredFace>> routed = [];
        int considered = 0, matched = 0;

        foreach (FaceRecord face in faces)
        {
            // Checked here rather than after the sweep. Routing every face
            // against every person is the whole cost of this handler, so a token
            // observed only once it had finished would make Stop mean nothing for
            // as long as the work actually takes.
            cancellationToken.ThrowIfCancellationRequested();

            if (!face.IsUnclaimed || face.DetectScore < MinimumProposalScore)
            {
                continue;
            }

            considered++;

            RoutedFace? best = FaceRouter.Route(
                face.TakenUtc,
                face.Embedding,
                candidates,
                personId => !refused.Contains((face.FaceId, personId)));

            if (best is not RoutedFace winner)
            {
                continue;
            }

            matched++;

            if (!routed.TryGetValue(winner.PersonId, out List<ScoredFace>? theirs))
            {
                theirs = [];
                routed[winner.PersonId] = theirs;
            }

            theirs.Add(new ScoredFace(face.FaceId, winner.Score));
        }

        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<int, PersonProposals> byPerson = [];
        int offered = 0;

        foreach ((int personId, List<ScoredFace> theirs) in routed)
        {
            ScoredFace[] top =
                [.. theirs.OrderByDescending(face => face.Score).Take(MaxProposals)];

            await _repository
                .AssignAsync(personId, top, AssignmentSource.Proposed, cancellationToken)
                .ConfigureAwait(false);

            byPerson[personId] = new PersonProposals(top.Length, theirs.Count);
            offered += top.Length;
        }

        return new ProposalRound(offered, matched, considered, byPerson);
    }
}

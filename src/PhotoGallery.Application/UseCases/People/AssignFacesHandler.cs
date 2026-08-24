using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>
/// Records who some faces are, then works out what that tells the app about the
/// rest of the library.
/// </summary>
/// <remarks>
/// Naming a group, confirming a proposal and rejecting one are the same
/// operation with a different answer, so they are one handler. Each of them
/// changes what the person's confirmed faces are, which changes their eras,
/// which changes what should be proposed next - and that chain is the whole
/// reason naming gets easier the more of it is done.
///
/// <para>Who gets offered what is not decided here. One person's examples cannot
/// answer a question about the whole field - a face that looks like them may
/// look a great deal more like somebody else - so proposing is a sweep over
/// everybody, and it lives in <see cref="ProposeFacesHandler"/>.</para>
/// </remarks>
public sealed class AssignFacesHandler
{
    private readonly IPeopleReader _people;
    private readonly IPeopleRepository _repository;
    private readonly ProposeFacesHandler _propose;

    public AssignFacesHandler(
        IPeopleReader people, IPeopleRepository repository, ProposeFacesHandler propose)
    {
        _people = people;
        _repository = repository;
        _propose = propose;
    }

    public async Task<AssignmentResult> HandleAsync(
        AssignFacesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.FaceIds.Count == 0)
        {
            throw new ArgumentException("No faces were named.", nameof(request));
        }

        int personId = request.PersonId
            ?? await _repository
                .EnsurePersonAsync(
                    request.DisplayName
                        ?? throw new ArgumentException(
                            "A name or a person is needed.", nameof(request)),
                    cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<int> answered = await EveryCopyOfAsync(
            request.FaceIds, personId, cancellationToken).ConfigureAwait(false);

        await _repository
            .AssignAsync(
                personId,
                [.. answered.Select(faceId => new ScoredFace(faceId))],
                request.Source,
                cancellationToken)
            .ConfigureAwait(false);

        // What is reported back is how many questions were answered, not how
        // many rows that came to - the user answered one face, whatever number
        // of files that photograph happens to exist as.
        return await RefreshAsync(personId, request.FaceIds.Count, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Widens an answer to every copy of the photograph it was given on.
    /// </summary>
    /// <remarks>
    /// A photograph in this library exists as up to eight files, and each file
    /// carries its own row for the same face. The question is asked once -
    /// <see cref="FaceOnPicture"/> collapses the copies for the queue, and the
    /// badge counts them collapsed - so the answer has to settle all of them.
    ///
    /// <para>Answering only the row that happened to stand for the group left
    /// the rest still proposed. The next round then put one of them up in its
    /// place: the same crop, at the same count, however many times it was
    /// answered - which is a button that looks broken.</para>
    ///
    /// <para>A copy already confirmed as somebody else is left alone: that is a
    /// decision the user made about another person, and widening an answer is
    /// not the place to overturn it. Everything else goes with the answer,
    /// including a copy refused earlier - the same face in the same picture
    /// cannot be them and not them, and the newer answer is the one they just
    /// gave. A face set aside as a stranger is not being offered to anybody and
    /// stays that way.</para>
    /// </remarks>
    private async Task<IReadOnlyList<int>> EveryCopyOfAsync(
        IReadOnlyList<int> faceIds, int personId, CancellationToken cancellationToken)
    {
        IReadOnlyList<FaceRecord> faces = await _people
            .GetFacesAsync(includeEmbeddings: false, cancellationToken)
            .ConfigureAwait(false);

        HashSet<int> asked = [.. faceIds];
        HashSet<(string ThumbnailName, FaceBounds Bounds)> places =
            new(FaceOnPicture.Comparer);

        foreach (FaceRecord face in faces.Where(face => asked.Contains(face.FaceId)))
        {
            places.Add((face.ThumbnailName, face.Bounds));
        }

        if (places.Count == 0)
        {
            return faceIds;
        }

        return
        [
            .. faces
                .Where(face => asked.Contains(face.FaceId) || (Free(face, personId)
                    && places.Contains((face.ThumbnailName, face.Bounds))))
                .Select(face => face.FaceId),
        ];
    }

    /// <summary>
    /// Whether an answer about one copy of a photograph may carry to this row.
    /// </summary>
    /// <remarks>
    /// A rejection reads back here as no claim at all, by design - refusing a
    /// face for one person leaves it free to be offered to another - so this
    /// cannot tell a refused copy from an untouched one, and does not need to.
    /// What it has to protect is a copy confirmed as somebody else.
    /// </remarks>
    private static bool Free(FaceRecord face, int personId) =>
        !face.IsIgnored
        && (face.Source != AssignmentSource.Confirmed || face.PersonId == personId);

    /// <summary>
    /// Rebuilds a person from the examples given for them, without changing any
    /// of those examples.
    /// </summary>
    /// <remarks>
    /// What "check again" does for one person. Naming a face in one photograph
    /// teaches the app what they looked like at that date; this is what takes
    /// that lesson out across the rest of the library.
    /// </remarks>
    public Task<AssignmentResult> RefreshAsync(
        int personId, CancellationToken cancellationToken = default) =>
        RefreshAsync(personId, assigned: 0, cancellationToken);

    /// <summary>
    /// Works out afresh what one person looked like over their life, from the
    /// faces confirmed as theirs.
    /// </summary>
    /// <remarks>
    /// Separate from proposing because the two happen at different rates: eras
    /// change when one person is answered, proposals have to be redecided for
    /// everybody whenever any of them do. Checking everyone rebuilds every
    /// person's eras and then sweeps once, rather than sweeping per person.
    /// </remarks>
    public async Task<int> RebuildErasAsync(
        int personId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FaceSample> confirmed =
            await _people.GetSamplesAsync(personId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PersonEra> eras = EraBuilder.Derive(confirmed);
        await _repository.ReplaceErasAsync(personId, [.. eras], cancellationToken)
            .ConfigureAwait(false);

        return eras.Count;
    }

    private async Task<AssignmentResult> RefreshAsync(
        int personId, int assigned, CancellationToken cancellationToken)
    {
        int eras = await RebuildErasAsync(personId, cancellationToken).ConfigureAwait(false);
        ProposalRound round = await _propose.HandleAsync(cancellationToken).ConfigureAwait(false);

        string displayName = await NameOfAsync(personId, cancellationToken).ConfigureAwait(false);
        PersonProposals theirs = round.For(personId);

        return new AssignmentResult(
            personId, displayName, assigned, eras,
            theirs.Offered, theirs.Matched, round.Considered);
    }

    private async Task<string> NameOfAsync(int personId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Person> people =
            await _people.GetPeopleAsync(cancellationToken).ConfigureAwait(false);

        return people.FirstOrDefault(person => person.Id == personId)?.DisplayName
            ?? string.Empty;
    }
}

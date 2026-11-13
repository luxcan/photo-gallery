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
        int personId = await RecordAsync(request, cancellationToken).ConfigureAwait(false);

        // What is reported back is how many questions were answered, not how
        // many rows that came to - the user answered one face, whatever number
        // of files that photograph happens to exist as.
        return await RefreshAsync(personId, request.FaceIds.Count, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records the answer and puts right whoever it took faces from, without
    /// working out what it means for the person named.
    /// </summary>
    /// <remarks>
    /// For answering a queue one face at a time, where rebuilding the person and
    /// sweeping the library after every click would make two hundred answers
    /// unusable - that consequence is left for later. What is not left is the
    /// person a confirmation took the face from: their eras were built with it,
    /// and only this knows who they were. Writing the answer straight to the
    /// index skipped them, so they went on being offered faces like one the
    /// user had just said was not them.
    /// </remarks>
    /// <returns>The person named, made first when the request names somebody new.</returns>
    public async Task<int> RecordAsync(
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

        // Only the photographs asked about: everything below looks for copies
        // of those, and the queue waits on this once per click.
        IReadOnlyList<FaceRecord> faces = await _people
            .GetFacesOnPicturesOfAsync(request.FaceIds, cancellationToken)
            .ConfigureAwait(false);

        // Read before the answer is written, because writing it is what takes
        // the faces off whoever they were confirmed as.
        ILookup<int, int> confirmedAs = await ConfirmationsOnAsync(
                faces, request.FaceIds, cancellationToken)
            .ConfigureAwait(false);

        bool correcting = request.Source == AssignmentSource.Confirmed;
        IReadOnlyList<int> answered =
            EveryCopyOf(faces, request.FaceIds, personId, confirmedAs, correcting);

        await _repository
            .AssignAsync(
                personId,
                [.. answered.Select(faceId => new ScoredFace(faceId))],
                request.Source,
                cancellationToken)
            .ConfigureAwait(false);

        // The repository took these faces off whoever they were confirmed as,
        // and those people's eras were built with them among the examples. Left
        // alone, they would go on being offered faces that look like one the
        // user has just said is not them.
        // Every one of them, read from the confirmations themselves: a face an
        // older build left confirmed as two people has just lost both.
        int[] movedFrom = correcting
            ? [.. answered.SelectMany(faceId => SomebodyElse(confirmedAs[faceId], personId)).Distinct()]
            : [];

        foreach (int previous in movedFrom)
        {
            await RebuildErasAsync(previous, cancellationToken).ConfigureAwait(false);
        }

        return personId;
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
    ///
    /// <para>The one exception is a correction: when the face asked about was
    /// itself confirmed as somebody, the copies of that same photograph
    /// confirmed as that same person were named with it, and move with it -
    /// otherwise one picture would show two names depending on which file is
    /// open. Only that photograph's: a correction made on one picture says
    /// nothing about a copy of another picture in the same batch, whoever that
    /// copy happens to be confirmed as.</para>
    /// </remarks>
    /// <param name="confirmedAs">
    /// Everybody each face was confirmed as before this answer.
    /// </param>
    /// <param name="correcting">
    /// Whether this answer is a confirmation, the only kind that takes a face
    /// off whoever it was confirmed as.
    /// </param>
    private static IReadOnlyList<int> EveryCopyOf(
        IReadOnlyList<FaceRecord> faces,
        IReadOnlyList<int> faceIds,
        int personId,
        ILookup<int, int> confirmedAs,
        bool correcting)
    {
        HashSet<int> asked = [.. faceIds];

        // Each photograph asked about, with whoever its asked face is being
        // taken off - kept per photograph, so one correction cannot open the
        // door to another photograph's copies.
        Dictionary<(string ThumbnailName, FaceBounds Bounds), HashSet<int>> places =
            new(FaceOnPicture.Comparer);

        foreach (FaceRecord face in faces.Where(face => asked.Contains(face.FaceId)))
        {
            if (!places.TryGetValue((face.ThumbnailName, face.Bounds), out HashSet<int>? previous))
            {
                previous = [];
                places.Add((face.ThumbnailName, face.Bounds), previous);
            }

            if (correcting)
            {
                previous.UnionWith(SomebodyElse(confirmedAs[face.FaceId], personId));
            }
        }

        if (places.Count == 0)
        {
            return faceIds;
        }

        return
        [
            .. faces
                .Where(face => asked.Contains(face.FaceId)
                    || (places.TryGetValue((face.ThumbnailName, face.Bounds), out HashSet<int>? previous)
                        && Free(face, personId, confirmedAs, previous)))
                .Select(face => face.FaceId),
        ];
    }

    /// <summary>
    /// Whether an answer about one copy of a photograph may carry to this row.
    /// </summary>
    /// <remarks>
    /// A rejection is not a confirmation, by design - refusing a face for one
    /// person leaves it free to be offered to another - so this cannot tell a
    /// refused copy from an untouched one, and does not need to. What it has to
    /// protect is a copy confirmed as somebody else - unless everybody it is
    /// confirmed as is somebody this answer is correcting on this photograph.
    /// </remarks>
    /// <param name="previous">
    /// Whoever the face asked about on this photograph is being taken off.
    /// </param>
    private static bool Free(
        FaceRecord face, int personId, ILookup<int, int> confirmedAs, IReadOnlySet<int> previous) =>
        !face.IsIgnored
        && SomebodyElse(confirmedAs[face.FaceId], personId).All(previous.Contains);

    /// <summary>Everybody in a list of people but the one now named.</summary>
    private static IEnumerable<int> SomebodyElse(IEnumerable<int> people, int personId) =>
        people.Where(person => person != personId);

    /// <summary>
    /// Everybody the faces asked about, and every copy of their photographs,
    /// are confirmed as.
    /// </summary>
    /// <remarks>
    /// Read from the confirmations rather than from <see cref="FaceRecord"/>,
    /// which carries one claim per face: a face an older build left confirmed as
    /// two people would show only one of them, and correcting it takes it off
    /// both.
    /// </remarks>
    private async Task<ILookup<int, int>> ConfirmationsOnAsync(
        IReadOnlyList<FaceRecord> faces,
        IReadOnlyList<int> faceIds,
        CancellationToken cancellationToken)
    {
        HashSet<int> asked = [.. faceIds];
        HashSet<(string ThumbnailName, FaceBounds Bounds)> places = new(
            faces
                .Where(face => asked.Contains(face.FaceId))
                .Select(face => (face.ThumbnailName, face.Bounds)),
            FaceOnPicture.Comparer);

        HashSet<int> onThosePictures =
        [
            .. asked,
            .. faces
                .Where(face => places.Contains((face.ThumbnailName, face.Bounds)))
                .Select(face => face.FaceId),
        ];

        IReadOnlyList<FaceConfirmation> confirmations = await _people
            .GetConfirmationsAsync(onThosePictures, cancellationToken)
            .ConfigureAwait(false);

        return confirmations.ToLookup(
            confirmation => confirmation.FaceId, confirmation => confirmation.PersonId);
    }

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

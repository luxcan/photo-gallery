using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>
/// Builds the People screen: who has been named, and how much of the library
/// they account for.
/// </summary>
/// <remarks>
/// It does not gather unnamed faces into groups to be named. That was tried and
/// removed: a group is the app's guess at a question, and the answer it wants -
/// "who is this?" - is one the user can give far better while looking at a
/// photograph they recognise. Naming happens in the viewer; this screen is the
/// register that results, and the place proposals are answered.
/// </remarks>
public sealed class GetPeopleBoardHandler
{
    private readonly IPeopleReader _people;

    public GetPeopleBoardHandler(IPeopleReader people) => _people = people;

    public async Task<PeopleBoard> HandleAsync(CancellationToken cancellationToken = default)
    {
        // No vectors: this screen counts faces and shows crops of them, and
        // nothing here compares one face against another.
        IReadOnlyList<FaceRecord> faces = await _people
            .GetFacesAsync(includeEmbeddings: false, cancellationToken)
            .ConfigureAwait(false);

        if (faces.Count == 0)
        {
            return PeopleBoard.Empty;
        }

        IReadOnlyList<Person> people =
            await _people.GetPeopleAsync(cancellationToken).ConfigureAwait(false);

        return new PeopleBoard(
            [.. people.Select(person => Summarise(person, faces))],
            faces.Count,
            faces.Count(face => face.IsNamed));
    }

    private static PersonSummary Summarise(Person person, IReadOnlyList<FaceRecord> faces)
    {
        List<FaceRecord> theirs = [.. faces.Where(face => face.PersonId == person.Id)];
        List<FaceRecord> confirmed =
            [.. theirs.Where(face => face.Source == AssignmentSource.Confirmed)];

        FaceRecord? cover = confirmed
            .OrderByDescending(face => face.Bounds.Area)
            .ThenByDescending(face => face.DetectScore)
            .FirstOrDefault();

        return new PersonSummary(
            person.Id,
            person.DisplayName,
            confirmed.Count,
            confirmed.Select(face => face.AssetId).Distinct().Count(),

            // Deduped exactly as the queue that answers them is, in
            // GetPersonReviewHandler. Counting rows instead over-reported every
            // person whose photographs exist as several files - on this library
            // one photograph exists as up to eight - so the badge promised more
            // questions than the screen went on to ask.
            theirs
                .Where(face => face.Source == AssignmentSource.Proposed)
                .DistinctBy(face => (face.ThumbnailName, face.Bounds), FaceOnPicture.Comparer)
                .Count(),
            cover is null
                ? null
                : new FaceThumbnail(
                    cover.FaceId, cover.AssetId, cover.ThumbnailName, cover.Bounds,
                    cover.TakenUtc, cover.RelativePath, cover.FullPath),
            person.BirthYear);
    }
}

using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>
/// What the app believes about one person: the faces it has been told are
/// theirs, and the ones it thinks are.
/// </summary>
public sealed class GetPersonReviewHandler
{
    /// <summary>How many confirmed faces the screen shows.</summary>
    /// <remarks>
    /// A person with a thousand confirmed faces does not need all of them drawn
    /// to show who they are; the proposals are the part that needs answering.
    /// </remarks>
    private const int ConfirmedShown = 60;

    private readonly IPeopleReader _people;

    public GetPersonReviewHandler(IPeopleReader people) => _people = people;

    public async Task<PersonReview?> HandleAsync(
        int personId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Person> people =
            await _people.GetPeopleAsync(cancellationToken).ConfigureAwait(false);

        Person? person = people.FirstOrDefault(candidate => candidate.Id == personId);
        if (person is null)
        {
            return null;
        }

        // No vectors: the review lists what is already decided about this person,
        // and the deciding happened in ProposeFacesHandler.
        IReadOnlyList<FaceRecord> faces = await _people
            .GetFacesAsync(includeEmbeddings: false, cancellationToken)
            .ConfigureAwait(false);

        List<FaceRecord> theirs = [.. faces.Where(face => face.PersonId == personId)];

        return new PersonReview(
            person.Id,
            person.DisplayName,
            [
                .. theirs
                    .Where(face => face.Source == AssignmentSource.Proposed)
                    .OrderByDescending(face => face.Bounds.Area)

                    // One per picture. Asking the same question once for every
                    // duplicate file is work the user should never be given.
                    .DistinctBy(face => (face.ThumbnailName, face.Bounds), FaceOnPicture.Comparer)
                    .Select(Thumbnail),
            ],
            [
                .. theirs
                    .Where(face => face.IsNamed)
                    .OrderBy(face => face.TakenUtc)
                    .DistinctBy(face => (face.ThumbnailName, face.Bounds), FaceOnPicture.Comparer)
                    .Take(ConfirmedShown)
                    .Select(Thumbnail),
            ],
            [.. person.Eras.OrderBy(era => era.FromUtc)]);
    }

    private static FaceThumbnail Thumbnail(FaceRecord face) =>
        new(face.FaceId, face.AssetId, face.ThumbnailName, face.Bounds,
            face.TakenUtc, face.RelativePath, face.FullPath);
}

using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>
/// One statement about who some faces are.
/// </summary>
/// <param name="DisplayName">
/// Names the person. A name that already exists is the same person - saying
/// "Ana Lim" over a second group is a statement that it is them again, not a
/// request for a second Ana Lim.
/// </param>
/// <param name="PersonId">
/// Names them by row instead, which is what confirming or rejecting a proposal
/// does. Exactly one of this and <paramref name="DisplayName"/> is given.
/// </param>
public sealed record AssignFacesRequest(
    IReadOnlyList<int> FaceIds,
    AssignmentSource Source,
    string? DisplayName = null,
    int? PersonId = null);

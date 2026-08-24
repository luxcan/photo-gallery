using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>One person, and what is outstanding about them.</summary>
public sealed record PersonReview(
    int PersonId,
    string DisplayName,
    IReadOnlyList<FaceThumbnail> Proposed,
    IReadOnlyList<FaceThumbnail> Confirmed,
    IReadOnlyList<PersonEra> Eras);

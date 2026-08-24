namespace PhotoGallery.Application.Ports;

/// <summary>Someone who has been named, and how much of the library they are in.</summary>
/// <param name="AwaitingReview">
/// How many questions are actually waiting - one per photograph, not one per
/// copy of it. The button that opens the queue is labelled from this, so it has
/// to count what the queue will ask.
/// </param>
public sealed record PersonSummary(
    int Id,
    string DisplayName,
    int ConfirmedFaces,
    int Photos,
    int AwaitingReview,
    FaceThumbnail? Cover,
    int? BirthYear = null);

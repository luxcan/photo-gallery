using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// One face in the photograph currently open, and who it is said to be.
/// </summary>
/// <remarks>
/// The bounds are in the pixels of the cached preview, because that is the image
/// the detector looked at and the one the viewer draws. Whoever draws the box has
/// to scale it to however large the picture is on screen.
/// </remarks>
public sealed record FaceOnPhoto(
    int FaceId,
    FaceBounds Bounds,
    float DetectScore,
    int? PersonId,
    string? PersonName,
    AssignmentSource? Source,
    bool IsIgnored)
{
    public bool IsNamed => Source == AssignmentSource.Confirmed;

    public bool IsProposed => Source == AssignmentSource.Proposed;
}

using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Application.Ports;

/// <summary>One face on a photograph the measuring step may need to read.</summary>
/// <param name="FaceId">The face row.</param>
/// <param name="Bounds">Its box, in the cached preview's pixels.</param>
/// <param name="Measured">Whether it has a measurement row at all.</param>
/// <param name="EyesRead">Whether that row holds an eye state, rather than none for want of the model.</param>
public sealed record FaceToMeasure(int FaceId, FaceBounds Bounds, bool Measured, bool EyesRead);

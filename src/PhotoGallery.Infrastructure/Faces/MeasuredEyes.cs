using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Infrastructure.Faces;

/// <summary>
/// One face's eye measurement together with the points it was read from.
/// </summary>
/// <param name="Measurement">What the port hands on.</param>
/// <param name="Landmarks">
/// The 106 points in the preview's pixels, x and y alternating, or null when
/// the face could not be measured. Kept for whoever needs to draw the outlines
/// to check them by eye.
/// </param>
public sealed record MeasuredEyes(EyeMeasurement Measurement, float[]? Landmarks);

namespace PhotoGallery.Domain.Faces;

/// <summary>
/// What was measured about one face's eyes, before anything is decided from it.
/// </summary>
/// <param name="LeftOpenness">
/// The eye on the left of the picture: the gap between its lids divided by its
/// width (the eye aspect ratio), or null when it could not be measured.
/// </param>
/// <param name="RightOpenness">The same for the eye on the right of the picture.</param>
/// <param name="Turn">
/// How far the face is turned from the camera, from the detector's five points:
/// how far the nose sits from the midpoint between the eyes, as a share of half
/// the distance between them. Zero is facing the camera; one is the nose in
/// line with an eye. Not a number when the points were not found.
/// </param>
/// <param name="Confidence">
/// How well the landmark model agrees with the detector about where the eyes
/// are, from zero (nowhere near) to one (exactly). The landmark model gives no
/// score of its own, and two models trained apart agreeing is the best evidence
/// available that either is looking at an eye.
/// </param>
/// <remarks>
/// Kept apart from <see cref="EyeVerdict"/> so phase 1 of PRP 14 can record the
/// raw numbers and choose the thresholds from them, rather than the thresholds
/// being guessed before anything was measured.
/// </remarks>
public readonly record struct EyeMeasurement(
    double? LeftOpenness,
    double? RightOpenness,
    double Turn,
    double Confidence)
{
    /// <summary>A face nothing could be measured on.</summary>
    public static EyeMeasurement Unmeasured { get; } = new(null, null, double.NaN, 0d);
}

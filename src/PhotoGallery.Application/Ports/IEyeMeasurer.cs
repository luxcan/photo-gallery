using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// Measures how open the eyes are on faces already found in a cached preview.
/// </summary>
/// <remarks>
/// Returns the measurement rather than a verdict: <see cref="EyeVerdict"/> holds
/// the thresholds and decides, so the rule stays pure and testable and the
/// raw numbers are there to choose those thresholds from (PRP 14, phase 1).
///
/// <para>Takes a path and returns plain data, as <see cref="IFaceScanner"/>
/// does, so whoever runs the pass keeps ownership of what is read and when.</para>
/// </remarks>
public interface IEyeMeasurer
{
    /// <summary>
    /// One measurement per face, in the order given, or <see langword="null"/>
    /// when the preview could not be read or the graphs would not run on it -
    /// nothing was learned, so the faces stay due to be measured.
    /// </summary>
    /// <param name="previewPath">The cached preview the faces were found on.</param>
    /// <param name="faces">The faces' boxes, in that preview's pixels.</param>
    /// <param name="cancellationToken">Stops the work between faces.</param>
    /// <remarks>
    /// A face that cannot be measured - the detector no longer finds it there,
    /// or its landmarks make no sense - comes back as
    /// <see cref="EyeMeasurement.Unmeasured"/>, which every verdict reads as
    /// Unknown.
    /// </remarks>
    Task<IReadOnlyList<EyeMeasurement>?> MeasureAsync(
        string previewPath,
        IReadOnlyList<FaceBounds> faces,
        CancellationToken cancellationToken = default);
}

using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// Sharpness and exposure read off a cached preview, for the keeper ladder of
/// PRP 14.
/// </summary>
/// <remarks>
/// Only ever the cached preview, never the original: the measuring step runs
/// with the photo sources out of reach, and the preview is what every copy of
/// every picture already has at one known size.
/// </remarks>
public interface IPreviewQualityReader
{
    /// <summary>The preview's measurements, or null when it will not open.</summary>
    /// <param name="previewPath">The cached preview.</param>
    /// <param name="faces">Face boxes to measure as well, in the preview's pixels.</param>
    PreviewReading? Read(string previewPath, IReadOnlyList<FaceBounds> faces);
}

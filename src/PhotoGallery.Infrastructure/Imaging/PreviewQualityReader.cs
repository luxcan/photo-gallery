using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Infrastructure.Imaging;

/// <inheritdoc cref="IPreviewQualityReader"/>
/// <remarks>
/// Exposure is read on the preview as it is, because a share of pixels does not
/// change with the size it is counted at; phase 1 found the same frame at two
/// sizes within 0.031. Sharpness is read at <see cref="PreviewQualityMeter.CommonEdge"/>,
/// because it does.
/// </remarks>
public sealed class PreviewQualityReader : IPreviewQualityReader
{
    public PreviewReading? Read(string previewPath, IReadOnlyList<FaceBounds> faces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewPath);
        ArgumentNullException.ThrowIfNull(faces);

        if (PreviewPixels.Read(previewPath) is not BgrImage preview)
        {
            return null;
        }

        PreviewExposure exposure = PreviewQualityMeter.Exposure(preview);

        if (PreviewQualityMeter.AtCommonSize(preview) is not (BgrImage sized, double scale))
        {
            return new PreviewReading(
                preview.Width, preview.Height, null, exposure.ClippedHigh, exposure.ClippedLow,
                [.. faces.Select(_ => (double?)null)]);
        }

        return new PreviewReading(
            preview.Width,
            preview.Height,
            PreviewQualityMeter.SceneSharpness(sized),
            exposure.ClippedHigh,
            exposure.ClippedLow,
            [
                .. faces.Select(face =>
                    PreviewQualityMeter.FaceSharpness(sized, [PreviewQualityMeter.Scaled(face, scale)])),
            ]);
    }
}

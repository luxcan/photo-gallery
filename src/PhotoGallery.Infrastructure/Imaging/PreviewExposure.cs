namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// How much of a preview is blown white or crushed black, each as a share of
/// its pixels from zero to one.
/// </summary>
public readonly record struct PreviewExposure(double ClippedHigh, double ClippedLow);

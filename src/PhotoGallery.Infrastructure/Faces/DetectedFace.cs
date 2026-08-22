namespace PhotoGallery.Infrastructure.Faces;

/// <summary>
/// One face the detector found, in the coordinates of the picture it was given.
/// </summary>
/// <param name="Landmarks">
/// Ten numbers: the x and y of the two eyes, the nose, then the two mouth
/// corners. They exist only to align the crop the recognition model sees, and
/// are not stored.
/// </param>
public sealed record DetectedFace(
    float Left,
    float Top,
    float Right,
    float Bottom,
    float Score,
    float[] Landmarks)
{
    public float Width => Right - Left;

    public float Height => Bottom - Top;

    /// <summary>The shorter edge, which is what "too small to be worth keeping" means.</summary>
    public float ShortEdge => MathF.Min(Width, Height);
}

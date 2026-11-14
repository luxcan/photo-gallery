namespace PhotoGallery.Infrastructure.Faces;

/// <summary>
/// Which way a face is pointing, read from the detector's five points.
/// </summary>
/// <remarks>
/// The five points are the picture-left eye, the picture-right eye, the nose
/// and the two corners of the mouth, x and y alternating - the order
/// <see cref="ScrfdFaceDetector"/> reports them in.
/// </remarks>
public static class FacePose
{
    /// <summary>
    /// How far the line through the eyes is turned from level, in radians,
    /// clockwise as the picture is drawn, or not a number when the eyes are in
    /// one place.
    /// </summary>
    public static float Roll(ReadOnlySpan<float> points)
    {
        RequireFivePoints(points);

        float x = points[2] - points[0];
        float y = points[3] - points[1];
        return x == 0f && y == 0f ? float.NaN : MathF.Atan2(y, x);
    }

    /// <summary>
    /// How far the face is turned from the camera: the nose's distance from the
    /// midpoint between the eyes, along the eye line, as a share of half the
    /// eyes' spread. Not a number when the eyes are in one place.
    /// </summary>
    /// <remarks>
    /// Measured along the eye line rather than across the picture, so a face
    /// lying on its side and looking at the camera reads as facing it. As a
    /// face turns, the eye nearer the camera stays put while the nose swings
    /// towards the far one, so the share grows with the turn, reaches one about
    /// when the far eye starts to disappear behind the nose, and does not
    /// depend on how large the face is in the picture.
    /// </remarks>
    public static double Turn(ReadOnlySpan<float> points)
    {
        RequireFivePoints(points);

        double axisX = points[2] - points[0], axisY = points[3] - points[1];
        double spread = Math.Sqrt((axisX * axisX) + (axisY * axisY));
        if (!(spread > 0d))
        {
            return double.NaN;
        }

        double middleX = (points[0] + points[2]) / 2d, middleY = (points[1] + points[3]) / 2d;
        double along = (((points[4] - middleX) * axisX) + ((points[5] - middleY) * axisY)) / spread;

        return Math.Abs(along) / (spread / 2d);
    }

    private static void RequireFivePoints(ReadOnlySpan<float> points)
    {
        if (points.Length != FaceAligner.LandmarkCount * 2)
        {
            throw new ArgumentException(
                $"Expected the detector's {FaceAligner.LandmarkCount} points, x and y alternating.",
                nameof(points));
        }
    }
}

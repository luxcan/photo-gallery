namespace PhotoGallery.Infrastructure.Faces;

/// <summary>
/// Which of the 106 landmarks outline each eye, and how open an outline is.
/// </summary>
/// <remarks>
/// The indices were read off real faces from this library, not taken from
/// memory: the 106 points were drawn with their numbers on crops of open, shut
/// and squinting eyes (PRP 14, phase 1). Each eye is eight points round its
/// lids and a ninth in the middle:
///
/// <code>
///            upper lid                     upper lid
///          41   40   42                  95   94   96
///     35        38        39        89        92        93
///          36   33   37                  90   87   91
///            lower lid                     lower lid
///      eye on the picture's left     eye on the picture's right
/// </code>
///
/// Each ring below starts at a corner, runs along the lower lid to the other
/// corner and back along the upper one, so the lid pairs that face each other
/// are always at positions 1 and 7, 2 and 6, 3 and 5.
/// </remarks>
public static class EyeOutline
{
    /// <summary>The eye on the left of the picture, round its lids.</summary>
    public static IReadOnlyList<int> PictureLeft { get; } = [35, 36, 33, 37, 39, 42, 40, 41];

    /// <summary>The eye on the right of the picture, round its lids.</summary>
    public static IReadOnlyList<int> PictureRight { get; } = [89, 90, 87, 91, 93, 96, 94, 95];

    /// <summary>The point in the middle of the eye on the left of the picture.</summary>
    public const int PictureLeftCentre = 38;

    /// <summary>The point in the middle of the eye on the right of the picture.</summary>
    public const int PictureRightCentre = 92;

    /// <summary>
    /// The gap between the lids divided by the eye's width - the eye aspect
    /// ratio - or null when the outline is too degenerate to say.
    /// </summary>
    /// <param name="landmarks">All 106 points, x and y alternating.</param>
    /// <param name="ring">One of <see cref="PictureLeft"/> or <see cref="PictureRight"/>.</param>
    /// <remarks>
    /// The gap is measured across the line between the two corners, not
    /// straight down the picture, so a head on one side does not read as a
    /// wider eye; and it is the mean of the three lid pairs, so one point
    /// placed badly moves it by a third rather than deciding it.
    /// </remarks>
    public static double? Openness(ReadOnlySpan<float> landmarks, IReadOnlyList<int> ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        RequireAllPoints(landmarks);

        if (ring.Count != 8)
        {
            throw new ArgumentException("An eye's ring is eight points round its lids.", nameof(ring));
        }

        (double startX, double startY) = At(landmarks, ring[0]);
        (double endX, double endY) = At(landmarks, ring[4]);
        double axisX = endX - startX, axisY = endY - startY;
        double width = Math.Sqrt((axisX * axisX) + (axisY * axisY));

        // Below a hundredth of a pixel there is no eye to divide by; the model
        // has collapsed the outline, which is a doubt rather than a shut eye.
        if (!(width > 0.01d))
        {
            return null;
        }

        double normalX = -axisY / width, normalY = axisX / width;
        double gaps = 0d;
        for (int pair = 1; pair <= 3; pair++)
        {
            (double lowerX, double lowerY) = At(landmarks, ring[pair]);
            (double upperX, double upperY) = At(landmarks, ring[8 - pair]);
            gaps += Math.Abs(((upperX - lowerX) * normalX) + ((upperY - lowerY) * normalY));
        }

        double openness = gaps / 3d / width;
        return double.IsFinite(openness) ? openness : null;
    }

    /// <summary>
    /// How closely the landmark model's eye centres sit on the detector's eye
    /// points, from zero to one.
    /// </summary>
    /// <param name="landmarks">All 106 points, x and y alternating.</param>
    /// <param name="detectorPoints">
    /// The detector's five points: picture-left eye, picture-right eye, nose,
    /// then the two mouth corners.
    /// </param>
    /// <remarks>
    /// One minus the mean distance between the two pairs of eye centres, as a
    /// share of the detector's eye spread, held at zero. The landmark model
    /// reports no score of its own; it places 106 points on whatever it is
    /// given, a hand or a sunglass lens included. The detector was trained apart
    /// from it, so where the two disagree about the eyes at least one of them is
    /// not looking at an eye.
    /// </remarks>
    public static double Agreement(ReadOnlySpan<float> landmarks, ReadOnlySpan<float> detectorPoints)
    {
        RequireAllPoints(landmarks);
        if (detectorPoints.Length != FaceAligner.LandmarkCount * 2)
        {
            throw new ArgumentException("The detector reports five points.", nameof(detectorPoints));
        }

        double spread = Distance(detectorPoints[0], detectorPoints[1], detectorPoints[2], detectorPoints[3]);
        if (!(spread > 0d))
        {
            return 0d;
        }

        (double leftX, double leftY) = At(landmarks, PictureLeftCentre);
        (double rightX, double rightY) = At(landmarks, PictureRightCentre);
        double miss = (Distance(leftX, leftY, detectorPoints[0], detectorPoints[1])
                     + Distance(rightX, rightY, detectorPoints[2], detectorPoints[3])) / 2d;

        double agreement = 1d - (miss / spread);
        return double.IsFinite(agreement) ? Math.Max(0d, agreement) : 0d;
    }

    private static void RequireAllPoints(ReadOnlySpan<float> landmarks)
    {
        if (landmarks.Length != FaceLandmarker.PointCount * 2)
        {
            throw new ArgumentException(
                $"Expected {FaceLandmarker.PointCount} points, x and y alternating.", nameof(landmarks));
        }
    }

    private static (double X, double Y) At(ReadOnlySpan<float> landmarks, int index) =>
        (landmarks[index * 2], landmarks[(index * 2) + 1]);

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)));
}

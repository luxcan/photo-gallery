namespace PhotoGallery.Infrastructure.Faces;

/// <summary>
/// Warps a detected face onto the fixed 112x112 layout the recognition model
/// was trained against.
/// </summary>
/// <remarks>
/// This is the part of the port most likely to be silently wrong. Get the
/// alignment slightly off and nothing throws: the embeddings look perfectly
/// reasonable and match the wrong people, which reads as the model being
/// imperfect rather than the code being broken. It is therefore pure, separate
/// from anything that touches a file or a model, and tested on its own.
/// </remarks>
public static class FaceAligner
{
    public const int CropSize = 112;

    /// <summary>How many points the detector reports per face.</summary>
    public const int LandmarkCount = 5;

    /// <summary>
    /// Where the five points have to land in the crop: eyes, nose, then the two
    /// mouth corners. These are the recognition model's own training layout, so
    /// they are constants rather than a choice.
    /// </summary>
    private static readonly float[] s_template =
    [
        38.2946f, 51.6963f,
        73.5318f, 51.5014f,
        56.0252f, 71.7366f,
        41.5493f, 92.3655f,
        70.7299f, 92.2041f,
    ];

    /// <summary>
    /// The least-squares similarity transform taking a face's five landmarks
    /// onto the template.
    /// </summary>
    /// <remarks>
    /// Closed form rather than a singular value decomposition. In two
    /// dimensions the best-fitting scaled rotation is one division: treat each
    /// point as a complex number and the fit is the ratio of the cross-
    /// correlation to the source's energy. That is the same answer the general
    /// algorithm gives, and the two can only disagree when the best fit would
    /// need a reflection - which for a face means a mirrored one, and would be
    /// the wrong answer anyway.
    /// </remarks>
    public static bool TryEstimate(ReadOnlySpan<float> landmarks, out SimilarityTransform transform)
    {
        transform = default;
        if (landmarks.Length != LandmarkCount * 2)
        {
            return false;
        }

        float sourceMeanX = 0f, sourceMeanY = 0f, targetMeanX = 0f, targetMeanY = 0f;
        for (int i = 0; i < LandmarkCount; i++)
        {
            sourceMeanX += landmarks[i * 2];
            sourceMeanY += landmarks[(i * 2) + 1];
            targetMeanX += s_template[i * 2];
            targetMeanY += s_template[(i * 2) + 1];
        }

        sourceMeanX /= LandmarkCount;
        sourceMeanY /= LandmarkCount;
        targetMeanX /= LandmarkCount;
        targetMeanY /= LandmarkCount;

        double dot = 0d, cross = 0d, energy = 0d;
        for (int i = 0; i < LandmarkCount; i++)
        {
            double sourceX = landmarks[i * 2] - sourceMeanX;
            double sourceY = landmarks[(i * 2) + 1] - sourceMeanY;
            double targetX = s_template[i * 2] - targetMeanX;
            double targetY = s_template[(i * 2) + 1] - targetMeanY;

            dot += (sourceX * targetX) + (sourceY * targetY);
            cross += (sourceX * targetY) - (sourceY * targetX);
            energy += (sourceX * sourceX) + (sourceY * sourceY);
        }

        if (energy <= 0d || !double.IsFinite(energy))
        {
            // Every landmark in the same place. Nothing sane can be fitted, and
            // a detection like that is not a face.
            return false;
        }

        float a = (float)(dot / energy);
        float b = (float)(cross / energy);
        if (!float.IsFinite(a) || !float.IsFinite(b) || (a == 0f && b == 0f))
        {
            return false;
        }

        transform = new SimilarityTransform(
            a,
            b,
            targetMeanX - ((a * sourceMeanX) - (b * sourceMeanY)),
            targetMeanY - ((b * sourceMeanX) + (a * sourceMeanY)));

        return true;
    }

    /// <summary>
    /// The 112x112 crop a face's landmarks map it onto, or null when they
    /// cannot be fitted.
    /// </summary>
    /// <remarks>
    /// Filled by reversing the transform and asking where each destination
    /// pixel came from, which is the only way to leave no holes. Anything
    /// falling outside the picture contributes black, so a face at the very
    /// edge of a photo still produces a crop of the expected size.
    /// </remarks>
    public static BgrImage? Align(BgrImage image, ReadOnlySpan<float> landmarks)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (!TryEstimate(landmarks, out SimilarityTransform transform)
            || !transform.TryInvert(out SimilarityTransform inverse))
        {
            return null;
        }

        byte[] crop = new byte[CropSize * CropSize * BgrImage.BytesPerPixel];
        Span<byte> topLeft = stackalloc byte[BgrImage.BytesPerPixel];
        Span<byte> topRight = stackalloc byte[BgrImage.BytesPerPixel];
        Span<byte> bottomLeft = stackalloc byte[BgrImage.BytesPerPixel];
        Span<byte> bottomRight = stackalloc byte[BgrImage.BytesPerPixel];

        for (int y = 0; y < CropSize; y++)
        {
            for (int x = 0; x < CropSize; x++)
            {
                (float sourceX, float sourceY) = inverse.Apply(x, y);

                int x0 = (int)MathF.Floor(sourceX);
                int y0 = (int)MathF.Floor(sourceY);
                float weightX = sourceX - x0;
                float weightY = sourceY - y0;

                image.Sample(x0, y0, topLeft);
                image.Sample(x0 + 1, y0, topRight);
                image.Sample(x0, y0 + 1, bottomLeft);
                image.Sample(x0 + 1, y0 + 1, bottomRight);

                int target = ((y * CropSize) + x) * BgrImage.BytesPerPixel;
                for (int channel = 0; channel < BgrImage.BytesPerPixel; channel++)
                {
                    float top = (topLeft[channel] * (1 - weightX)) + (topRight[channel] * weightX);
                    float bottom =
                        (bottomLeft[channel] * (1 - weightX)) + (bottomRight[channel] * weightX);

                    crop[target + channel] =
                        (byte)Math.Clamp(MathF.Round((top * (1 - weightY)) + (bottom * weightY)), 0, 255);
                }
            }
        }

        return new BgrImage(crop, CropSize, CropSize);
    }
}

using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Tests;

/// <summary>
/// Embeddings laid out around a circle in one plane, so the similarity between
/// any two of them is exactly the cosine of the angle between them.
/// </summary>
/// <remarks>
/// That makes every expectation in a grouping or era test arithmetic rather than
/// a guess: faces ten degrees apart score 0.98, sixty degrees apart score 0.5,
/// and ninety are unrelated.
/// </remarks>
internal static class TestEmbeddings
{
    public static FaceEmbedding At(double degrees)
    {
        double radians = degrees * Math.PI / 180d;
        float[] values = new float[FaceEmbedding.Dimensions];
        values[0] = (float)Math.Cos(radians);
        values[1] = (float)Math.Sin(radians);
        return new FaceEmbedding(values);
    }
}

using System.Runtime.InteropServices;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// A face vector as its raw little-endian bytes: 2 KB, and no parsing either
/// way.
/// </summary>
/// <remarks>
/// One encoding for the two places that need one - the index stores it in a
/// column and a decision set carries it in a file - because two of them would
/// eventually disagree, and an embedding that comes back subtly wrong does not
/// fail. It returns a confident answer about the wrong person.
/// </remarks>
public static class FaceEmbeddingBytes
{
    /// <summary>How many bytes one vector takes.</summary>
    public const int Length = FaceEmbedding.Dimensions * sizeof(float);

    public static byte[] From(in FaceEmbedding embedding)
    {
        var bytes = new byte[Length];
        MemoryMarshal.AsBytes(embedding.Values).CopyTo(bytes);
        return bytes;
    }

    public static FaceEmbedding To(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var values = new float[FaceEmbedding.Dimensions];
        MemoryMarshal.Cast<byte, float>(bytes).CopyTo(values);
        return new FaceEmbedding(values);
    }
}

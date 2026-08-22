using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a 512-dimension embedding as its raw little-endian bytes: 2 KB per
/// face, and no parsing cost when the whole set is loaded for a search.
/// </summary>
public sealed class FaceEmbeddingConverter : ValueConverter<FaceEmbedding, byte[]>
{
    public FaceEmbeddingConverter()
        : base(embedding => ToBytes(embedding), bytes => FromBytes(bytes))
    {
    }

    private static byte[] ToBytes(FaceEmbedding embedding)
    {
        var bytes = new byte[FaceEmbedding.Dimensions * sizeof(float)];
        MemoryMarshal.AsBytes(embedding.Values).CopyTo(bytes);
        return bytes;
    }

    private static FaceEmbedding FromBytes(byte[] bytes)
    {
        var values = new float[FaceEmbedding.Dimensions];
        MemoryMarshal.Cast<byte, float>(bytes).CopyTo(values);
        return new FaceEmbedding(values);
    }
}

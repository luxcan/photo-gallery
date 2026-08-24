using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PhotoGallery.Domain.Search;

namespace PhotoGallery.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a 768-dimension content embedding as its raw little-endian bytes:
/// 3 KB per photograph, and no parsing cost when the whole set is loaded to
/// answer a search.
/// </summary>
public sealed class ContentEmbeddingConverter : ValueConverter<ContentEmbedding, byte[]>
{
    public ContentEmbeddingConverter()
        : base(embedding => ToBytes(embedding), bytes => FromBytes(bytes))
    {
    }

    private static byte[] ToBytes(ContentEmbedding embedding)
    {
        var bytes = new byte[ContentEmbedding.Dimensions * sizeof(float)];
        MemoryMarshal.AsBytes(embedding.Values).CopyTo(bytes);
        return bytes;
    }

    private static ContentEmbedding FromBytes(byte[] bytes)
    {
        var values = new float[ContentEmbedding.Dimensions];
        MemoryMarshal.Cast<byte, float>(bytes).CopyTo(values);
        return new ContentEmbedding(values);
    }
}

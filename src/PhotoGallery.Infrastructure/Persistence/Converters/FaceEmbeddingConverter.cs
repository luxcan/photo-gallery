using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a 512-dimension embedding as its raw little-endian bytes: 2 KB per
/// face, and no parsing cost when the whole set is loaded for a search.
/// </summary>
/// <remarks>
/// The encoding itself lives in <see cref="FaceEmbeddingBytes"/>, shared with
/// the one a decision set is written in. Two of them would eventually disagree,
/// and an embedding that comes back subtly wrong does not fail - it answers
/// confidently about the wrong person.
/// </remarks>
public sealed class FaceEmbeddingConverter : ValueConverter<FaceEmbedding, byte[]>
{
    public FaceEmbeddingConverter()
        : base(embedding => FaceEmbeddingBytes.From(embedding),
               bytes => FaceEmbeddingBytes.To(bytes))
    {
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>Writes a face vector as base64 of the same bytes the index stores.</summary>
/// <remarks>
/// 512 floats written as JSON numbers would be about 6 KB of text per era, and
/// this is 2.7 KB - but the size is not the reason. A vector rounded on the way
/// through a decimal form does not fail; it answers confidently about the wrong
/// person, which is the one failure this feature must not have.
/// </remarks>
public sealed class FaceEmbeddingJsonConverter : JsonConverter<FaceEmbedding>
{
    public override FaceEmbedding Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        byte[]? bytes = reader.GetBytesFromBase64();

        if (bytes is null || bytes.Length != FaceEmbeddingBytes.Length)
        {
            throw new JsonException("That is not a face vector.");
        }

        return FaceEmbeddingBytes.To(bytes);
    }

    public override void Write(
        Utf8JsonWriter writer, FaceEmbedding value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // An era with no centroid is not something this writes, but a struct has
        // a default and a file that threw on one would be a file nobody could
        // read back.
        writer.WriteBase64StringValue(
            value.IsEmpty ? new byte[FaceEmbeddingBytes.Length] : FaceEmbeddingBytes.From(value));
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>Writes a face's key as the one string the key itself defines.</summary>
public sealed class FaceKeyJsonConverter : JsonConverter<FaceKey>
{
    public override FaceKey Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            return FaceKey.Parse(reader.GetString() ?? string.Empty);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(
        Utf8JsonWriter writer, FaceKey value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>Writes a photograph's key as the one string the key itself defines.</summary>
/// <remarks>
/// One string rather than an object, because this is the most repeated thing in
/// the file - once per answer, and there are 9,455 of those on this library -
/// and an object would spend a field name on each half of every one.
/// </remarks>
public sealed class AssetKeyJsonConverter : JsonConverter<AssetKey>
{
    public override AssetKey Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            return AssetKey.Parse(reader.GetString() ?? string.Empty);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(
        Utf8JsonWriter writer, AssetKey value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// A decision set as it sits in the shared folder: JSON, gzipped.
/// </summary>
/// <remarks>
/// Measured on this library: 9,455 human answers about 5,906 photographs, 15
/// people, 25 eras and 6 turns come to 1.23 MB of JSON and <strong>419 KB
/// gzipped</strong>, plus 50 KB of era centroids. Twelve years of naming faces
/// weighs under half a megabyte, which is the measurement the whole feature is
/// shaped around.
///
/// <para>Gzipped rather than left as text because most of the file is the same
/// few thousand path strings repeated, which is exactly what a compressor is
/// for - a third of the size for a few milliseconds, on a link measured at
/// 6.4 MB/s.</para>
///
/// <para>Read defensively. Everything here came off a shared drive that another
/// machine may be writing to at this moment, so a file that is half written, in
/// a shape this release does not know, or simply not JSON at all must be
/// reported rather than thrown past the exchange.</para>
/// </remarks>
public static class DecisionSetFile
{
    /// <summary>What a published file is called, after the machine that wrote it.</summary>
    public const string Extension = ".json.gz";

    /// <summary>
    /// How a decision is written down, wherever it is written down.
    /// </summary>
    /// <remarks>
    /// Shared with the held-answer rows rather than left to the file. A key is
    /// a struct with no parameterless constructor and a compact text form, so
    /// serialising one without these converters produces something that cannot
    /// be read back at all - and an answer parked in a shape nothing can parse
    /// is an answer quietly lost, which is the one thing holding it exists to
    /// prevent.
    /// </remarks>
    internal static JsonSerializerOptions Shape { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new AssetKeyJsonConverter(),
            new FaceKeyJsonConverter(),
            new FaceEmbeddingJsonConverter(),
            new JsonStringEnumConverter(),
        },
    };

    public static async Task WriteAsync(
        Stream destination, DecisionSet decisions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(decisions);

        // Left open: the caller owns the stream and usually still has to flush
        // it to disk and rename it into place.
        await using var gzip = new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: true);
        await JsonSerializer
            .SerializeAsync(gzip, decisions, Shape, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<DecisionSet> ReadAsync(
        Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);

        return await JsonSerializer
            .DeserializeAsync<DecisionSet>(gzip, Shape, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new JsonException("The file held no answers.");
    }
}

using System.IO.Compression;
using System.Text.Json;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// One gzipped JSON file read out of the sync folder, no bigger than it could
/// honestly be.
/// </summary>
/// <remarks>
/// Anybody who can write to the folder can leave a file there, and every scan
/// and every Sync reads what is there. So each kind of file has a ceiling, set
/// far above what a real library writes and far below what would take the app
/// down. A file over its ceiling is one more file that could not be read - the
/// same as one caught half written - not a reason to close the app.
///
/// <para>One number per kind serves both sides of the gzip. The packed file is
/// refused without being opened when it is bigger than the ceiling, which
/// costs nothing: JSON always packs smaller than itself, so a packed file that
/// size could never unpack inside the ceiling anyway.</para>
/// </remarks>
internal static class SharedFolderFile
{
    private const long Megabyte = 1024 * 1024;

    /// <summary>The ceiling for one machine's answers.</summary>
    /// <remarks>
    /// Measured here: 9,455 answers about 5,906 photographs unpack to 1.23 MB.
    /// 64 MB is fifty libraries' worth of naming faces.
    /// </remarks>
    public const long AnswersLimit = 64 * Megabyte;

    /// <summary>The ceiling for one machine's prepared facts.</summary>
    /// <remarks>
    /// Measured here: 15,823 rows unpack to 4.20 MB, about 270 bytes a row.
    /// 128 MB is close to half a million photographs.
    /// </remarks>
    public const long FactsLimit = 128 * Megabyte;

    /// <summary>The ceiling for one machine's face vectors.</summary>
    /// <remarks>
    /// The big one, and the reason the ceilings differ. A face is a 2 KB vector
    /// written as 2.7 KB of base64, which barely packs: 40.5 MB here, about
    /// 2.9 KB a face. 512 MB is around 180,000 faces - twelve times this
    /// library's twelve years. Higher would let one planted string cost
    /// gigabytes of memory before the reader gave up on it.
    /// </remarks>
    public const long FacesLimit = 512 * Megabyte;

    /// <summary>
    /// Reads one file, refusing it as unreadable the moment it proves bigger
    /// than <paramref name="limit"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is over the ceiling, or not gzip.</exception>
    /// <exception cref="JsonException">The file is not JSON of this shape, or holds nothing.</exception>
    public static async Task<T> ReadAsync<T>(
        Stream source, long limit, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.CanSeek && source.Length - source.Position > limit)
        {
            throw new InvalidDataException(
                $"The file is more than {limit / Megabyte} MB, far more than any library writes.");
        }

        await using var bounded = new LimitedReadStream(
            new GZipStream(source, CompressionMode.Decompress, leaveOpen: true), limit);

        return await JsonSerializer
            .DeserializeAsync<T>(bounded, DecisionSetFile.Shape, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new JsonException("The file held nothing.");
    }
}

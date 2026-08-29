using System.Buffers.Binary;
using System.Text.Json;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>What one machine says to another over a direct connection.</summary>
public enum PeerAsk
{
    /// <summary>Who are you, and what version of this app are you running.</summary>
    Hello = 0,

    /// <summary>Here is my half of a pairing check value.</summary>
    Pair = 1,

    /// <summary>Send me everything you have decided.</summary>
    Decisions = 2,
}

/// <summary>One message, in the shape both ends read it.</summary>
/// <param name="Detail">
/// Free text belonging to the ask: a pairing check value, or the reason
/// something was refused. Never a decision - those travel as the gzipped body
/// that follows.
/// </param>
public sealed record PeerMessage(
    PeerAsk Ask,
    Guid MachineId,
    string Name,
    string AppVersion,
    int SchemaVersion,
    string Detail,
    bool Refused);

/// <summary>
/// The framing: a length and then that many bytes.
/// </summary>
/// <remarks>
/// Four bytes big-endian and no more, because a stream has no message
/// boundaries and something has to say where one ends. Every read is bounded by
/// the length that preceded it, so a peer cannot make this machine allocate a
/// gigabyte by claiming to be about to send one.
/// </remarks>
public static class PeerFraming
{
    /// <summary>
    /// The most one message may be.
    /// </summary>
    /// <remarks>
    /// A decision set is 469 KB on this library and grows with what people have
    /// decided, not with how many photographs there are. Sixty-four megabytes is
    /// two orders of magnitude of headroom and still small enough that a machine
    /// claiming it cannot hurt this one.
    /// </remarks>
    public const int Most = 64 * 1024 * 1024;

    public static async Task WriteAsync(
        Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(payload);

        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);

        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one frame, or null when the other end has finished.</summary>
    public static async Task<byte[]?> ReadAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] header = new byte[4];

        if (!await FillAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        int length = BinaryPrimitives.ReadInt32BigEndian(header);

        if (length is < 0 or > Most)
        {
            throw new InvalidDataException(
                $"The other computer offered a message of {length} bytes.");
        }

        byte[] payload = new byte[length];

        return await FillAsync(stream, payload, cancellationToken).ConfigureAwait(false)
            ? payload
            : null;
    }

    public static Task WriteAsync(
        Stream stream, PeerMessage message, CancellationToken cancellationToken) =>
        WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(message), cancellationToken);

    public static async Task<PeerMessage?> ReadMessageAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        byte[]? payload = await ReadAsync(stream, cancellationToken).ConfigureAwait(false);

        if (payload is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PeerMessage>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<bool> FillAsync(
        Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int filled = 0;

        while (filled < buffer.Length)
        {
            int read = await stream
                .ReadAsync(buffer.AsMemory(filled), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                return false;
            }

            filled += read;
        }

        return true;
    }
}

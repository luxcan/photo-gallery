using System.Buffers.Binary;
using System.IO;
using System.Security;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Reads the turn an MP4 or MOV file asks for, from the header of its video
/// track.
/// </summary>
/// <remarks>
/// Written here rather than taken from a media library, because the answer is
/// four numbers in one fixed place and every clip in the first real library
/// keeps them the same way: of 7,871 files read, every one with a movie box
/// had exactly one video track, a version 0 track header, and no fragments.
/// The movie box sat at the end of 5,995 of them, behind the film, so it is
/// found by stepping over the top-level boxes rather than reading through them
/// - over a share, a clip is hundreds of megabytes and its header is tens of
/// kilobytes.
///
/// <para>Only the four exact upright turns are understood. A mirrored or
/// scaled matrix reads as "cannot say", because drawing a clip a quarter turn
/// wrong on a guess is worse than the shape guess the caller falls back to.</para>
/// </remarks>
public sealed class Mp4VideoOrientationReader : IVideoOrientationReader
{
    /// <summary>
    /// The largest movie box this will load.
    /// </summary>
    /// <remarks>
    /// The largest in the first real library is 135 KB; a box claiming more than
    /// this is a damaged file, and loading it would be a large read over a share
    /// for nothing.
    /// </remarks>
    private const long LargestMovie = 64L * 1024 * 1024;

    private const uint Moov = 0x6D6F6F76;
    private const uint Trak = 0x7472616B;
    private const uint Tkhd = 0x746B6864;
    private const uint Mdia = 0x6D646961;
    private const uint Hdlr = 0x68646C72;
    private const uint Vide = 0x76696465;

    /// <summary>One whole unit of the matrix, which is fixed point 16.16.</summary>
    private const int One = 0x10000;

    public VideoOrientation? Read(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        try
        {
            // Shared for writing and deleting as well as reading: the player has
            // this same file open while it is read, and nothing here may stand in
            // the way of the user moving or removing it.
            using var file = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.RandomAccess);

            return ReadMovie(file) is byte[] movie ? FromMovie(movie) : null;
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException
                                      or SecurityException)
        {
            // A share that is away lands here as well as a damaged file, and
            // both are "cannot say" - never "upright", which is an answer.
            return null;
        }
    }

    /// <summary>
    /// The movie box, read whole, or null when the file has none this will load.
    /// </summary>
    private static byte[]? ReadMovie(FileStream file)
    {
        long length = file.Length;
        Span<byte> header = stackalloc byte[16];

        for (long at = 0; length - at >= 8;)
        {
            file.Position = at;
            file.ReadExactly(header[..8]);

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            int headerLength = 8;

            if (size == 1)
            {
                // A box too big for 32 bits - the film itself, in a long clip -
                // carries its real size in the eight bytes after the type.
                if (length - at < 16)
                {
                    return null;
                }

                file.ReadExactly(header[8..16]);
                ulong large = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                if (large > long.MaxValue)
                {
                    return null;
                }

                size = (long)large;
                headerLength = 16;
            }
            else if (size == 0)
            {
                // The last box, running to the end of the file.
                size = length - at;
            }

            // A box that claims more than the file holds is a file cut short.
            if (size < headerLength || size > length - at)
            {
                return null;
            }

            if (type == Moov)
            {
                if (size > LargestMovie)
                {
                    return null;
                }

                byte[] movie = new byte[size];
                file.Position = at;
                file.ReadExactly(movie);
                return movie;
            }

            at += size;
        }

        return null;
    }

    private static VideoOrientation? FromMovie(ReadOnlySpan<byte> movie)
    {
        if (!TryBox(movie, 0, movie.Length, out Box moov))
        {
            return null;
        }

        // The first video track. A sound track has a header with a matrix too,
        // always upright, and reading it would say every clip was the right way
        // up.
        for (int at = moov.Body; TryBox(movie, at, moov.End, out Box child); at = child.End)
        {
            if (child.Type == Trak && IsVideo(movie, child))
            {
                return FromTrackHeader(movie, child);
            }
        }

        return null;
    }

    private static bool IsVideo(ReadOnlySpan<byte> movie, Box track) =>
        Find(movie, track, Mdia) is Box media
        && Find(movie, media, Hdlr) is Box handler
        && handler.End - handler.Body >= 12
        && BinaryPrimitives.ReadUInt32BigEndian(movie[(handler.Body + 8)..]) == Vide;

    /// <summary>
    /// The turn and stored size in a track header (ISO/IEC 14496-12, 8.3.2).
    /// </summary>
    /// <remarks>
    /// After the version and flags come the creation and change times, the
    /// track number, a reserved word and the length: 20 bytes in version 0 and
    /// 32 in version 1, where the times and length are 64 bits. Then 8 reserved
    /// bytes and 8 more for layer, group, volume and padding - so the matrix is
    /// 40 bytes into the body in version 0 and 52 in version 1, which is 48 and
    /// 60 from the start of an ordinary box. Width and height follow the matrix,
    /// both 16.16.
    /// </remarks>
    private static VideoOrientation? FromTrackHeader(ReadOnlySpan<byte> movie, Box track)
    {
        if (Find(movie, track, Tkhd) is not Box header || header.End - header.Body < 4)
        {
            return null;
        }

        int matrix = movie[header.Body] switch
        {
            0 => header.Body + 40,
            1 => header.Body + 52,
            _ => -1,
        };

        if (matrix < 0 || header.End - matrix < 44)
        {
            return null;
        }

        int a = BinaryPrimitives.ReadInt32BigEndian(movie[matrix..]);
        int b = BinaryPrimitives.ReadInt32BigEndian(movie[(matrix + 4)..]);
        int c = BinaryPrimitives.ReadInt32BigEndian(movie[(matrix + 12)..]);
        int d = BinaryPrimitives.ReadInt32BigEndian(movie[(matrix + 16)..]);

        int width = (int)(BinaryPrimitives.ReadUInt32BigEndian(movie[(matrix + 36)..]) >> 16);
        int height = (int)(BinaryPrimitives.ReadUInt32BigEndian(movie[(matrix + 40)..]) >> 16);

        if (width <= 0 || height <= 0 || Turn(a, b, c, d) is not int degrees)
        {
            return null;
        }

        return new VideoOrientation(degrees, width, height);
    }

    /// <summary>
    /// The clockwise quarter turn a matrix describes, or null for anything else.
    /// </summary>
    /// <remarks>
    /// A phone held upright writes 0, 1, -1, 0 - a quarter clockwise - which is
    /// how the shell reads it when it makes the poster.
    /// </remarks>
    private static int? Turn(int a, int b, int c, int d) => (a, b, c, d) switch
    {
        (One, 0, 0, One) => 0,
        (0, One, -One, 0) => 90,
        (-One, 0, 0, -One) => 180,
        (0, -One, One, 0) => 270,
        _ => null,
    };

    private static Box? Find(ReadOnlySpan<byte> movie, Box parent, uint type)
    {
        for (int at = parent.Body; TryBox(movie, at, parent.End, out Box child); at = child.End)
        {
            if (child.Type == type)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>
    /// The box starting at <paramref name="at"/>, if a whole one fits before
    /// <paramref name="end"/>.
    /// </summary>
    private static bool TryBox(ReadOnlySpan<byte> movie, int at, int end, out Box box)
    {
        box = default;
        if (end - at < 8)
        {
            return false;
        }

        long size = BinaryPrimitives.ReadUInt32BigEndian(movie[at..]);
        uint type = BinaryPrimitives.ReadUInt32BigEndian(movie[(at + 4)..]);
        int headerLength = 8;

        if (size == 1)
        {
            if (end - at < 16)
            {
                return false;
            }

            ulong large = BinaryPrimitives.ReadUInt64BigEndian(movie[(at + 8)..]);
            size = large > int.MaxValue ? -1 : (long)large;
            headerLength = 16;
        }
        else if (size == 0)
        {
            size = end - at;
        }

        if (size < headerLength || size > end - at)
        {
            return false;
        }

        box = new Box(type, at + headerLength, at + (int)size);
        return true;
    }

    /// <summary>A box's four-letter type and where its contents start and end.</summary>
    private readonly record struct Box(uint Type, int Body, int End);
}

using System.Buffers.Binary;
using System.Text;

namespace PhotoGallery.Tests;

/// <summary>
/// MP4 and MOV files built byte by byte, for tests that read which way up a
/// clip goes.
/// </summary>
/// <remarks>
/// Built here rather than copied from a camera, so no family film ends up in
/// the repository and every field the reader looks at is one the test chose.
/// The layout follows ISO/IEC 14496-12 field by field, independently of the
/// reader's own offsets, so a wrong offset there cannot be agreed with here.
/// </remarks>
internal static class TestMp4
{
    /// <summary>A file of these top-level boxes, in this order.</summary>
    public static byte[] File(params byte[][] boxes) => [.. boxes.SelectMany(box => box)];

    /// <summary>The usual opening box of an MP4.</summary>
    public static byte[] FileType(string brand = "isom") =>
        Box("ftyp", Ascii(brand), new byte[4], Ascii(brand));

    /// <summary>A stand-in for the film itself.</summary>
    public static byte[] Film(int length = 4096) => Box("mdat", new byte[length]);

    /// <summary>A movie box holding these tracks.</summary>
    public static byte[] Movie(params byte[][] tracks) =>
        Box("moov", [Box("mvhd", new byte[100]), .. tracks]);

    /// <summary>A video track whose header asks for this turn.</summary>
    public static byte[] VideoTrack(int degrees, int width = 1920, int height = 1080, int version = 0)
    {
        (int a, int b, int c, int d) = Matrix(degrees);
        return Track("vide", TrackHeader(version, a, b, c, d, width, height));
    }

    /// <summary>A sound track, whose header is always upright and has no size.</summary>
    public static byte[] SoundTrack() =>
        Track("soun", TrackHeader(0, One, 0, 0, One, 0, 0));

    /// <summary>A track: its header, then the media box naming what it carries.</summary>
    public static byte[] Track(string handler, byte[] trackHeader) =>
        Box("trak", trackHeader, Box("mdia", Box("mdhd", new byte[24]), Handler(handler)));

    /// <summary>
    /// A track header (8.3.2): version and flags, the times, track number,
    /// reserved and length - 64-bit times and length in version 1 - then eight
    /// reserved bytes, layer, group, volume, two more reserved, the nine-word
    /// matrix, and width and height in 16.16.
    /// </summary>
    public static byte[] TrackHeader(int version, int a, int b, int c, int d, int width, int height)
    {
        var body = new List<byte> { (byte)version, 0, 0, 7 };
        int timesLength = version == 1 ? 8 + 8 + 4 + 4 + 8 : 4 + 4 + 4 + 4 + 4;
        body.AddRange(new byte[timesLength]);
        body.AddRange(new byte[8]);
        body.AddRange(new byte[2 + 2 + 2 + 2]);

        foreach (int word in new[] { a, b, 0, c, d, 0, 0, 0, 0x40000000 })
        {
            body.AddRange(Int32(word));
        }

        body.AddRange(Int32(width << 16));
        body.AddRange(Int32(height << 16));
        return Box("tkhd", [.. body]);
    }

    /// <summary>The matrix a camera writes for a clockwise quarter turn.</summary>
    public static (int A, int B, int C, int D) Matrix(int degrees) => degrees switch
    {
        0 => (One, 0, 0, One),
        90 => (0, One, -One, 0),
        180 => (-One, 0, 0, -One),
        270 => (0, -One, One, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(degrees)),
    };

    /// <summary>One whole unit in 16.16 fixed point.</summary>
    public const int One = 0x10000;

    /// <summary>A box with an ordinary 32-bit size.</summary>
    public static byte[] Box(string type, params byte[][] contents)
    {
        byte[] body = [.. contents.SelectMany(part => part)];
        return [.. Int32(8 + body.Length), .. Ascii(type), .. body];
    }

    /// <summary>
    /// A box whose size is given in the 64 bits after its type, the way a film
    /// over four gigabytes has to be.
    /// </summary>
    public static byte[] LargeBox(string type, params byte[][] contents)
    {
        byte[] body = [.. contents.SelectMany(part => part)];
        byte[] size = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)(16 + body.Length));
        return [.. Int32(1), .. Ascii(type), .. size, .. body];
    }

    /// <summary>A box with a size of zero, which runs to the end of the file.</summary>
    public static byte[] OpenEndedBox(string type, params byte[][] contents) =>
        [.. Int32(0), .. Ascii(type), .. contents.SelectMany(part => part)];

    private static byte[] Handler(string handler) =>
        Box("hdlr", new byte[4], new byte[4], Ascii(handler), new byte[12], [0]);

    private static byte[] Int32(int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}

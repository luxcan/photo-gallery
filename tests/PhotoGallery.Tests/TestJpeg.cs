using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoGallery.Tests;

/// <summary>
/// A real JPEG of a given size, for tests whose pictures are genuinely decoded
/// or turned.
/// </summary>
/// <remarks>
/// Patterned rather than blank, so that a turn moves pixels that can be told
/// apart. One copy for the App tests, which had grown two private ones and were
/// about to grow a third.
/// </remarks>
internal static class TestJpeg
{
    public static byte[] Bytes(int width, int height)
    {
        byte[] pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 251);
        }

        BitmapSource source = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Turning one decoded image into the two sizes the working folder keeps.
/// </summary>
/// <remarks>
/// Shared because a video's frames are renditions like any other. What arrives
/// differs - a photograph is decoded from its own bytes, a frame is lifted out
/// of a container - but from that point the sizes, the quality and the encoder
/// must be the same, or the grid would draw videos and photographs to different
/// standards.
/// </remarks>
internal static class Renditions
{
    /// <summary>
    /// Shrinks to fit <paramref name="maxEdge"/>, and never enlarges.
    /// </summary>
    /// <remarks>
    /// Returning the source untouched when it is already small enough matters
    /// for video: a phone clip is often 720 tall, so its preview is the frame
    /// itself and scaling it up would cost pixels without adding any.
    /// </remarks>
    public static BitmapSource Scale(BitmapSource source, int maxEdge)
    {
        double scale = Math.Min(
            1d,
            (double)maxEdge / Math.Max(source.PixelWidth, source.PixelHeight));

        if (scale >= 1d)
        {
            return source;
        }

        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    public static byte[] Encode(BitmapSource image, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}

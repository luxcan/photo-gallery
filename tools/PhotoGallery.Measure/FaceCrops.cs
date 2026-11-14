using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Measure;

/// <summary>
/// Writes each known face as a 2x enlargement with both eye outlines drawn
/// faintly over it, for labelling open, closed or can't tell by eye.
/// </summary>
/// <remarks>
/// WPF drawing, so it must run on a single-threaded apartment thread.
/// </remarks>
internal static class FaceCrops
{
    private const double Enlargement = 2d;

    /// <summary>A quarter of the box on every side, so the brows and cheeks show.</summary>
    private const double Margin = 0.25d;

    private static readonly Pen s_outline = Frozen(new Pen(
        new SolidColorBrush(Color.FromArgb(140, 0, 255, 0)), 1d));

    public static void Write(BgrImage preview, FaceBounds box, float[]? landmarks, string path)
    {
        int left = Math.Max(0, (int)(box.X - (box.Width * Margin)));
        int top = Math.Max(0, (int)(box.Y - (box.Height * Margin)));
        int right = Math.Min(preview.Width, (int)Math.Ceiling(box.X + box.Width + (box.Width * Margin)));
        int bottom = Math.Min(preview.Height, (int)Math.Ceiling(box.Y + box.Height + (box.Height * Margin)));
        if (right <= left || bottom <= top)
        {
            return;
        }

        BitmapSource source = BitmapSource.Create(
            preview.Width,
            preview.Height,
            96,
            96,
            PixelFormats.Bgr24,
            null,
            preview.Pixels,
            preview.Width * BgrImage.BytesPerPixel);
        var cropped = new CroppedBitmap(source, new Int32Rect(left, top, right - left, bottom - top));

        int width = (int)((right - left) * Enlargement);
        int height = (int)((bottom - top) * Enlargement);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawImage(cropped, new Rect(0, 0, width, height));

            if (landmarks is not null)
            {
                foreach (IReadOnlyList<int> ring in new[] { EyeOutline.PictureLeft, EyeOutline.PictureRight })
                {
                    context.DrawGeometry(null, s_outline, Outline(landmarks, ring, left, top));
                }
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static StreamGeometry Outline(float[] landmarks, IReadOnlyList<int> ring, int left, int top)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(At(landmarks, ring[0], left, top), isFilled: false, isClosed: true);
            for (int i = 1; i < ring.Count; i++)
            {
                context.LineTo(At(landmarks, ring[i], left, top), isStroked: true, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point At(float[] landmarks, int index, int left, int top) =>
        new((landmarks[index * 2] - left) * Enlargement, (landmarks[(index * 2) + 1] - top) * Enlargement);

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}

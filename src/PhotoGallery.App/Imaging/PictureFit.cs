using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoGallery.App.Imaging;

/// <summary>
/// Where a picture lands when it is drawn to fit an area, and therefore where a
/// box measured in the picture's own pixels lands with it.
/// </summary>
/// <remarks>
/// Both places that outline a face draw the picture uniformly - centred, with
/// bars on two sides - so the scale is whichever edge runs out first and the
/// offset is half of what is left over. The detector worked in the cached
/// preview's pixels and the picture on screen is whatever the window allows, so
/// this arithmetic is the whole of what keeps a box over a face; having one copy
/// of it is what keeps the viewer and the People screen agreeing.
/// </remarks>
public readonly record struct PictureFit(double Scale, double OffsetX, double OffsetY)
{
    /// <summary>
    /// How a picture fits an area, or null when there is nothing to place against.
    /// </summary>
    /// <remarks>
    /// Null rather than an identity fit: a picture that has not arrived yet and a
    /// picture drawn at its own size are different situations, and only the
    /// second one should move any boxes.
    /// </remarks>
    public static PictureFit? Of(double areaWidth, double areaHeight, ImageSource? picture)
    {
        if (areaWidth <= 0 || areaHeight <= 0
            || picture is not BitmapSource bitmap
            || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return null;
        }

        double scale = Math.Min(areaWidth / bitmap.PixelWidth, areaHeight / bitmap.PixelHeight);

        return new PictureFit(
            scale,
            (areaWidth - (bitmap.PixelWidth * scale)) / 2d,
            (areaHeight - (bitmap.PixelHeight * scale)) / 2d);
    }
}

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.App.Imaging;

/// <summary>
/// Loads a cached rendition into a frozen image the UI can show.
/// </summary>
/// <remarks>
/// Every choice here was measured, and each rules out a specific failure.
///
/// <para><c>BitmapCacheOption.OnLoad</c> decodes at <c>EndInit</c> and releases
/// the file: with <c>OnDemand</c> the bitmap holds the file open, and a
/// thumbnail pass writing that same tile fails with a sharing violation.</para>
///
/// <para>The stream is opened by hand with <c>FileShare.ReadWrite</c> because a
/// reader asking only for <c>FileShare.Read</c> is refused while a writer holds
/// the file - and <c>UriSource</c> gives no control over the share mode.</para>
///
/// <para>No <c>DecodePixelWidth</c>. The tile is already 400px on its longest
/// edge, so a fixed width upscales portrait tiles - measured 869 KB against
/// 522 KB for the same picture, for no gain in quality.</para>
///
/// <para>No <c>BitmapCreateOptions.IgnoreImageCache</c>: combined with a stream
/// it throws, because WPF's image cache is keyed on <c>UriSource</c> and there
/// is none. A stream never enters that cache, so there is nothing stale to
/// avoid.</para>
/// </remarks>
public static class TileImageLoader
{
    /// <summary>
    /// The tile for a picture, or null when there is nothing readable to show.
    /// </summary>
    public static ImageSource? LoadTile(IThumbnailStore store, string? thumbnailName)
    {
        ArgumentNullException.ThrowIfNull(store);

        return string.IsNullOrWhiteSpace(thumbnailName)
            ? null
            : Load(() => store.ResolveTilePath(thumbnailName));
    }

    /// <summary>The larger rendition, for looking at one picture.</summary>
    public static ImageSource? LoadPreview(IThumbnailStore store, string? thumbnailName)
    {
        ArgumentNullException.ThrowIfNull(store);

        return string.IsNullOrWhiteSpace(thumbnailName)
            ? null
            : Load(() => store.ResolvePreviewPath(thumbnailName));
    }

    /// <summary>
    /// One face, cut out of the preview the detector looked at.
    /// </summary>
    /// <remarks>
    /// Cut at the moment it is shown rather than written out as a file of its
    /// own. Eighteen thousand crops would be a second copy of the library's
    /// faces to keep in step with the renditions they came from, and the
    /// rendition is already local and already the right size.
    ///
    /// <para>The detected box stops at the chin and the hairline, which is
    /// enough for a model and too tight for a person - so it is opened out by a
    /// third before cutting, which is what makes a strip of crops recognisable
    /// rather than a row of noses.</para>
    ///
    /// <para>The result is a copy, and a small one. A <c>CroppedBitmap</c> keeps
    /// its source alive, so a screen holding several hundred faces would be
    /// holding several hundred whole previews behind them - the same unbounded
    /// growth the gallery hit at 2,133 MB, arrived at from a different
    /// direction. Copying the cut pixels at the size they will be drawn bounds
    /// it at well under a hundred megabytes.</para>
    /// </remarks>
    public static ImageSource? LoadFaceCrop(
        IThumbnailStore store, string? thumbnailName, FaceBounds bounds, double margin = 0.6d)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (string.IsNullOrWhiteSpace(thumbnailName))
        {
            return null;
        }

        return Load(() => store.ResolvePreviewPath(thumbnailName)) is BitmapSource preview
            ? CutFaceFrom(preview, bounds, margin)
            : null;
    }

    /// <summary>
    /// One face, cut from a preview that has already been decoded.
    /// </summary>
    /// <remarks>
    /// Separate from the overload above so that a screenful of faces out of the
    /// same photograph costs one decode rather than one each. A group shot can
    /// contribute eight proposals, and reading and decoding its preview eight
    /// times to cut eight rectangles out of it is the same work done seven times
    /// for nothing.
    /// </remarks>
    public static ImageSource? CutFaceFrom(
        BitmapSource preview, FaceBounds bounds, double margin = 0.6d)
    {
        ArgumentNullException.ThrowIfNull(preview);

        try
        {
            int padX = (int)Math.Round(bounds.Width * margin);
            int padY = (int)Math.Round(bounds.Height * margin);

            int left = Math.Clamp(bounds.X - padX, 0, preview.PixelWidth - 1);
            int top = Math.Clamp(bounds.Y - padY, 0, preview.PixelHeight - 1);
            int right = Math.Clamp(bounds.X + bounds.Width + padX, left + 1, preview.PixelWidth);
            int bottom = Math.Clamp(bounds.Y + bounds.Height + padY, top + 1, preview.PixelHeight);

            BitmapSource cut = new CroppedBitmap(
                preview, new Int32Rect(left, top, right - left, bottom - top));

            int longest = Math.Max(cut.PixelWidth, cut.PixelHeight);
            if (longest > FaceCropEdge)
            {
                double scale = (double)FaceCropEdge / longest;
                cut = new TransformedBitmap(cut, new ScaleTransform(scale, scale));
            }

            // Copies the pixels, which is what lets the preview behind them go.
            var owned = new WriteableBitmap(cut);
            owned.Freeze();
            return owned;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                      or OverflowException)
        {
            // Bounds recorded against a rendition that has since been replaced by
            // a smaller one. A grey square, not a broken screen.
            return null;
        }
    }

    /// <summary>
    /// How large a face crop is kept, on its longest edge.
    /// </summary>
    /// <remarks>
    /// The review grid draws them at 96 device-independent pixels, so this
    /// leaves room for a high-density display and nothing more.
    /// </remarks>
    private const int FaceCropEdge = 192;

    /// <summary>
    /// Resolving the path happens inside the guard, so this really is the single
    /// place a bad rendition can fail.
    /// </summary>
    private static ImageSource? Load(Func<string> resolvePath)
    {
        try
        {
            using FileStream file = new(
                resolvePath(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = file;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or NotSupportedException
                                      or ArgumentException
                                      or FormatException
                                      or COMException)
        {
            // A missing, half-written or corrupt rendition costs one grey cell,
            // never the grid. FormatException is in the list deliberately:
            // System.IO.FileFormatException, which WIC raises for a truncated
            // JPEG, derives from it and not from IOException - and a tile
            // truncated by an interrupted pass is exactly what a resumable
            // feature leaves behind.
            return null;
        }
    }
}

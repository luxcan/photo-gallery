namespace PhotoGallery.App.Gallery;

/// <summary>
/// The grid's geometry, in one place because it is shared by the view and by the
/// arithmetic that keeps the scroll position across a resize.
/// </summary>
/// <remarks>
/// A row of pictures is the unit the list virtualises, so every row must be
/// exactly the same height. If the XAML and this class ever disagreed the
/// scrollbar would drift - over 2,297 rows a four-pixel difference is nine
/// thousand pixels of error - so the XAML binds these values rather than
/// repeating them.
///
/// <para>The cell size is a parameter rather than a constant because the user can
/// zoom. It is passed to every calculation instead of being held here: a static
/// class with mutable state would let the view and the scroll arithmetic read
/// different sizes in the same frame, which is the drift above with extra steps.</para>
/// </remarks>
public static class GalleryLayout
{
    /// <summary>
    /// Square cells, because the aspect ratio of a picture is not reliably
    /// known: dimensions are absent until it has been prepared. A square crop
    /// needs no such data and keeps every row the same height.
    /// </summary>
    public const double DefaultCellSize = 150d;

    /// <summary>
    /// The same as the default: zoom goes up from where the grid opens, never
    /// down.
    /// </summary>
    /// <remarks>
    /// Tied to the default rather than set apart from it, so the grid opens at
    /// its densest and the worst case for memory is the state it starts in -
    /// there is no combination of stops that costs more than the first screen.
    /// That matters because a tile is decoded at its native 400px whatever size
    /// it is drawn at, <c>TileImageLoader</c> setting no <c>DecodePixelWidth</c>
    /// on purpose: smaller cells hold more bitmaps rather than cheaper ones.
    /// </remarks>
    public const double MinCellSize = DefaultCellSize;

    /// <summary>
    /// Comfortably inside the tile rendition's 400px longest edge, so the grid
    /// never upscales and a thumbnail is never soft.
    /// </summary>
    /// <remarks>
    /// The hard bound is 400 - past that the tiles have no more detail to give -
    /// but the last stops before it barely moved the row: on a 3,440px screen 300
    /// gives ten columns, 350 gives nine and 400 gives eight. Three stops to lose
    /// two columns is a ladder that stops doing anything near the top.
    /// </remarks>
    public const double MaxCellSize = 300d;

    public const double CellGap = 8d;

    /// <summary>
    /// Discrete stops rather than a continuous scale, so every cell lands on a
    /// whole pixel and one notch of the wheel is one visible change.
    /// </summary>
    /// <remarks>
    /// Four stops - 150, 200, 250, 300 - evenly spaced on purpose: the arithmetic
    /// below walks the ladder rather than holding a list, so a stop that did not
    /// fall on the step could never be reached.
    /// </remarks>
    public const double ZoomStep = 50d;

    public static double RowHeight(double cellSize) => cellSize + CellGap;

    /// <summary>How many cells fit, never fewer than one.</summary>
    public static int ColumnsFor(double availableWidth, double cellSize) =>
        Math.Max(1, (int)((availableWidth + CellGap) / (cellSize + CellGap)));

    /// <summary>
    /// The index of the first picture on the row at a given scroll offset, so a
    /// resize or a zoom can put the same picture back under the user's eye.
    /// </summary>
    public static int FirstItemAt(double verticalOffset, int columns, double cellSize) =>
        Math.Max(0, (int)(verticalOffset / RowHeight(cellSize))) * Math.Max(1, columns);

    public static double OffsetOf(int itemIndex, int columns, double cellSize) =>
        itemIndex / Math.Max(1, columns) * RowHeight(cellSize);

    /// <summary>
    /// Whether a scroll position is the place already being worked on, rather
    /// than a new one.
    /// </summary>
    /// <remarks>
    /// A virtualised list does not know how tall it is: WPF estimates the extent
    /// from the rows realised so far and refines the estimate as more of them
    /// realise. Every refinement moves the scroll position slightly and raises a
    /// scroll event, at a complete standstill - so a grid that treated each one
    /// as a move restarted its wait for the viewport to settle over and over,
    /// the wait never finished, and the pictures never decoded.
    ///
    /// <para>Less than one row apart is the test, because that cannot change
    /// which pictures are on screen by more than the margin already covers.</para>
    /// </remarks>
    public static bool IsSamePlace(int previousItem, int newItem, int columns) =>
        previousItem >= 0 && Math.Abs(newItem - previousItem) < Math.Max(1, columns);

    /// <summary>
    /// How many rows of pictures a window of this height shows at once.
    /// </summary>
    /// <remarks>
    /// One more than fits, because the grid does not scroll a row at a time: at
    /// almost every position there is a part row at the top and another at the
    /// bottom, and both are on screen.
    /// </remarks>
    public static int RowsOnScreen(double viewportHeight, double cellSize) =>
        Math.Max(1, (int)(Math.Max(0, viewportHeight) / RowHeight(cellSize)) + 1);

    /// <summary>
    /// The nearest usable cell size to the one asked for, snapped to a stop.
    /// </summary>
    /// <remarks>
    /// A stored preference can be anything - a database restored from an older
    /// release, a stop that a later one no longer offers, or a hand edit - and a
    /// cell the tiles cannot fill must never reach the grid. Zero is the case
    /// that matters: it is what a column default would hand back if the model
    /// ever stopped declaring one.
    /// </remarks>
    public static double Normalise(double cellSize)
    {
        if (!double.IsFinite(cellSize) || cellSize <= 0)
        {
            return DefaultCellSize;
        }

        double clamped = Math.Clamp(cellSize, MinCellSize, MaxCellSize);
        double steps = Math.Round((clamped - MinCellSize) / ZoomStep);
        return MinCellSize + (steps * ZoomStep);
    }

    /// <summary>
    /// One notch of the wheel. A positive direction zooms in, and the ends hold
    /// rather than wrapping.
    /// </summary>
    public static double Zoomed(double cellSize, int direction) =>
        Normalise(Normalise(cellSize) + (Math.Sign(direction) * ZoomStep));
}

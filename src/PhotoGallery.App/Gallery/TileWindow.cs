using System.Collections.ObjectModel;
using System.Windows.Media;
using PhotoGallery.App.Imaging;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Gallery;

/// <summary>
/// A grid of picture tiles, and the window of them that is decoded at any
/// moment.
/// </summary>
/// <remarks>
/// <para>
/// Virtualising the rows keeps the visual tree small but does nothing about the
/// bitmaps: a decoded 400px tile is about half a megabyte of unmanaged memory
/// that the garbage collector cannot see, so a grid that decoded every picture
/// it scrolled past reached 2.1 GB on this library and was heading for roughly
/// 7.6 GB.
/// </para>
/// <para>
/// So a window of pictures follows the viewport. The margin either side is
/// generous enough that normal scrolling never outruns it, and a tile that comes
/// back into view decodes again in about a fifth of a millisecond.
/// </para>
/// <para>
/// This lives apart from any one screen because there are now two grids - the
/// library, and one person's pictures - and there must not be two copies of the
/// only thing standing between this app and a two-gigabyte grid.
/// </para>
/// </remarks>
public sealed class TileWindow
{
    /// <summary>
    /// How many tiles are decoded at once. Measured at 0.20 ms per tile at this
    /// width on a local disk, so a screenful arrives well inside a frame.
    /// </summary>
    private const int DecodeParallelism = 4;

    /// <summary>
    /// How many pictures the window shows at once, until the view says.
    /// </summary>
    /// <remarks>
    /// Only a starting guess, for the first load before any layout has happened.
    /// It used to be the whole answer, and on a maximised window on a wide screen
    /// - twenty-one columns by nine rows, so 189 on screen - it decoded well
    /// under half of what the user was looking at and left the rest grey.
    /// </remarks>
    private const int DefaultOnScreen = 80;

    /// <summary>
    /// How long a screen must be looked at before its pictures are decoded.
    /// </summary>
    /// <remarks>
    /// A second: long enough that a screen merely scrolled past is never paid
    /// for, which on a library of this size is most of them. The cost is that
    /// stopping deliberately also waits a second before the tiles fill, so this
    /// is a judgement about which is more annoying rather than a measurement.
    /// </remarks>
    private static readonly TimeSpan VisibleSettle = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long before the pages either side are decoded too.
    /// </summary>
    /// <remarks>
    /// Prefetching is what makes scrolling on smooth, and it is also pure waste
    /// for a screen nobody stayed on - so it waits until staying is settled.
    /// </remarks>
    private static readonly TimeSpan MarginSettle = TimeSpan.FromMilliseconds(1_500);

    private readonly IThumbnailStore _thumbnails;

    private List<GalleryTile> _tiles = [];
    private IReadOnlyList<TileGroup> _groups = [];
    private int _columns = 1;
    private int _visibleRows;
    private int _windowStart;
    private CancellationTokenSource? _rangeCancellation;

    /// <summary>
    /// The position the request in flight is for, or -1 when there is none.
    /// </summary>
    private int _requestedStart = -1;

    public TileWindow(IThumbnailStore thumbnails)
    {
        ArgumentNullException.ThrowIfNull(thumbnails);
        _thumbnails = thumbnails;
    }

    /// <summary>The rows the list binds to.</summary>
    public ObservableCollection<GalleryRow> Rows { get; } = [];

    public IReadOnlyList<GalleryTile> Tiles => _tiles;

    public int Count => _tiles.Count;

    public GalleryTile this[int index] => _tiles[index];

    public int IndexOf(GalleryTile tile) => _tiles.IndexOf(tile);

    public int Columns => _columns;

    public int WindowStart => _windowStart;

    /// <summary>What is actually on screen, in pictures.</summary>
    public int OnScreenAllowance => _visibleRows <= 0
        ? DefaultOnScreen
        : Math.Max(1, _columns) * _visibleRows;

    /// <summary>
    /// Half a screen kept decoded either side of the visible one, so scrolling
    /// in either direction lands on pictures that are already drawn.
    /// </summary>
    /// <remarks>
    /// Two screens' worth in total. At roughly half a megabyte of unmanaged
    /// memory per tile that is about 80 MB for an ordinary window and about
    /// 190 MB for a maximised one on a wide screen, however large the library
    /// is - against the 2.1 GB the grid reached before any of this existed.
    /// Anything outside the window is released; coming back to it costs a fifth
    /// of a millisecond per tile to decode again.
    /// </remarks>
    public int WindowMargin => Math.Max(1, OnScreenAllowance / 2);

    /// <summary>Replaces everything, as one run under no heading.</summary>
    public void Fill(IEnumerable<GalleryTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        Fill([new TileGroup(null, null, null, [.. tiles])]);
    }

    /// <summary>Replaces everything, cut into headed runs.</summary>
    public void Fill(IReadOnlyList<TileGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        _groups = groups;
        _tiles = [.. groups.SelectMany(group => group.Tiles)];
        _requestedStart = -1;
        _windowStart = 0;
        Rebuild();
    }

    /// <summary>
    /// Re-cuts the runs without disturbing the tiles, for when only the grouping
    /// changed. Typing a year of birth regroups a person's pictures, and the
    /// bitmaps already decoded must survive it.
    /// </summary>
    public void Regroup(IReadOnlyList<TileGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        _groups = groups;
        _tiles = [.. groups.SelectMany(group => group.Tiles)];
        _requestedStart = -1;
        Rebuild();
    }

    /// <summary>
    /// Tells the grid how tall the window is, in rows of pictures.
    /// </summary>
    /// <remarks>
    /// Rows rather than pictures, because the number across is this class's own
    /// and changes with every resize; multiplying here keeps the two from
    /// disagreeing.
    /// </remarks>
    public void SetVisibleRows(int rows)
    {
        int settled = Math.Clamp(rows, 1, 200);
        if (settled == _visibleRows)
        {
            return;
        }

        _visibleRows = settled;

        // The grid asks for its first screen before the window has been laid
        // out, so that first ask is made against a guess. Asking again once the
        // real height is known is what stops a tall window opening on four rows
        // of pictures and a screenful of grey below them.
        _requestedStart = -1;
        if (_tiles.Count > 0)
        {
            _ = ShowRangeAsync(_windowStart);
        }
    }

    /// <summary>
    /// Re-chunks the rows for a new width, keeping the same tiles so nothing
    /// already decoded is thrown away.
    /// </summary>
    public void SetColumns(int columns)
    {
        if (columns < 1 || columns == _columns)
        {
            return;
        }

        _columns = columns;

        // A different number across is a different screenful, however little the
        // scroll position moved.
        _requestedStart = -1;
        Rebuild();
    }

    /// <summary>Forgets the position in flight, so the next ask is honoured.</summary>
    public void ForgetRequestedPosition() => _requestedStart = -1;

    /// <summary>
    /// Cuts the tiles into rows, restarting at every group so that no row
    /// straddles two headings.
    /// </summary>
    private void Rebuild()
    {
        Rows.Clear();

        int index = 0;
        foreach (TileGroup group in _groups)
        {
            bool first = true;
            for (int start = 0; start < group.Tiles.Count; start += _columns)
            {
                int take = Math.Min(_columns, group.Tiles.Count - start);
                List<GalleryTile> row = [.. group.Tiles.Skip(start).Take(take)];

                Rows.Add(new GalleryRow(row, index)
                {
                    Heading = first ? group.Heading : null,
                    HeadingDetail = first ? group.Detail : null,
                    HeadingNote = first ? group.Note : null,
                });

                first = false;
                index += take;
            }
        }
    }

    public async Task ShowRangeAsync(
        int firstVisibleItem, CancellationToken cancellationToken = default)
    {
        // Clamped before anything is derived from it. A scroll position can name
        // a row beyond the end - a grid that has just been re-chunked or emptied
        // reports one - and every range below is built from this number.
        int first = Math.Clamp(firstVisibleItem, 0, Math.Max(0, _tiles.Count - 1));

        // The same place reported again while its pictures are already being
        // fetched. Cancelling and restarting for these is what kept the wait
        // from ever finishing.
        if (_rangeCancellation is not null
            && GalleryLayout.IsSamePlace(_requestedStart, first, _columns))
        {
            return;
        }

        var mine = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _rangeCancellation?.Cancel();
        _rangeCancellation = mine;
        _requestedStart = first;

        // Read once. The first load runs before the window has been laid out, so
        // this number changes underneath a request that read it twice - and the
        // range released would then not be the range decoded.
        int onScreen = OnScreenAllowance;
        int margin = Math.Max(1, onScreen / 2);

        int from = Math.Max(0, first - margin);
        int to = Math.Min(_tiles.Count, first + onScreen + margin);

        // Released straight away rather than after the pause. This is what bounds
        // memory, it costs nothing, and a drag that never settles must not be
        // able to leave the whole library decoded behind it.
        for (int i = 0; i < _tiles.Count; i++)
        {
            if (i < from || i >= to)
            {
                _tiles[i].Picture = null;
            }
        }

        _windowStart = first;

        DiagnosticLog.Write(
            $"range asked at {first} of {_tiles.Count} "
            + $"(on screen {onScreen}, window {from}..{to})");

        try
        {
            // Stage one: what is actually on screen, after a pause short enough
            // that letting go of the scrollbar feels immediate.
            await Task.Delay(VisibleSettle, mine.Token);

            int visibleTo = Math.Min(_tiles.Count, first + onScreen);
            List<GalleryTile> visible = Missing(first, visibleTo);
            DiagnosticLog.Write($"range {first}: decoding {visible.Count} on screen");

            if (visible.Count > 0)
            {
                await LoadPicturesAsync(visible, mine.Token);
            }

            DiagnosticLog.Write($"range {first}: on screen done");

            // Stage two: the pages either side, so scrolling on lands on
            // pictures that are already drawn. Only for a viewport that has
            // stayed put - reading a screen means this is worth having, while
            // passing through it does not.
            await Task.Delay(MarginSettle - VisibleSettle, mine.Token);

            List<GalleryTile> ahead = Missing(visibleTo, to);
            List<GalleryTile> behind = Missing(from, first);
            behind.Reverse();

            // Below first, and each side worked outwards from the edge of the
            // screen: scrolling on is more common than scrolling back, and the
            // nearest picture is the one about to be needed.
            List<GalleryTile> around = [.. ahead, .. behind];
            if (around.Count > 0)
            {
                await LoadPicturesAsync(around, mine.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // The viewport moved on. Whatever this position wanted is no longer
            // what is being looked at.
            DiagnosticLog.Write($"range {first}: abandoned, the viewport moved");
        }
        finally
        {
            if (ReferenceEquals(_rangeCancellation, mine))
            {
                _rangeCancellation = null;
            }

            mine.Dispose();
        }
    }

    /// <summary>The pictures in a range that are not decoded yet.</summary>
    public List<GalleryTile> Missing(int from, int to)
    {
        List<GalleryTile> missing = [];
        for (int i = from; i < to; i++)
        {
            if (!_tiles[i].HasPicture)
            {
                missing.Add(_tiles[i]);
            }
        }

        return missing;
    }

    /// <summary>
    /// The pictures near the viewport that are still waiting for a rendition.
    /// </summary>
    /// <remarks>
    /// Only the pictures near the viewport. Retrying the whole library would
    /// decode every tile that has ever been prepared and undo the bound that
    /// keeps memory flat.
    /// </remarks>
    public List<GalleryTile> WaitingNearTheViewport()
    {
        int from = Math.Max(0, _windowStart - WindowMargin);
        int to = Math.Min(_tiles.Count, _windowStart + OnScreenAllowance + WindowMargin);
        return Missing(from, to);
    }

    /// <summary>
    /// Decodes off the dispatcher and hands each finished picture back through a
    /// <see cref="Progress{T}"/> created here, which is what marshals it to the
    /// UI thread - the same mechanism the scan already uses.
    /// </summary>
    public async Task LoadPicturesAsync(
        IReadOnlyList<GalleryTile> tiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        // Only the picture. Counting was called from here for every tile that
        // arrived, and the summary it recomputes walks all 11,482 rows asking
        // whether each is prepared - which decoding one tile cannot change. Two
        // hundred and forty tiles therefore cost two and a half million
        // comparisons on the dispatcher, and the scrolling they were meant to
        // keep up with is what paid for it.
        var arrived = new Progress<(GalleryTile Tile, ImageSource Picture)>(
            result => result.Tile.Picture = result.Picture);

        try
        {
            await Parallel.ForEachAsync(
                tiles,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = DecodeParallelism,
                    CancellationToken = cancellationToken,
                },
                (tile, token) =>
                {
                    ImageSource? picture =
                        TileImageLoader.LoadTile(_thumbnails, tile.ThumbnailName);
                    if (picture is not null)
                    {
                        ((IProgress<(GalleryTile, ImageSource)>)arrived).Report((tile, picture));
                    }

                    return ValueTask.CompletedTask;
                });
        }
        catch (OperationCanceledException)
        {
            // The view moved on; the tiles already decoded stay.
        }
    }

    /// <summary>
    /// Asks the disk which pictures actually have a rendition.
    /// </summary>
    /// <remarks>
    /// One existence check per picture. Measured at 691 ms for this library,
    /// which is why it runs on a worker: done on the dispatcher, and repeated on
    /// every progress report of a running pass, it left the window unresponsive.
    ///
    /// <para>The row's name alone will not do: a working folder can be copied or
    /// cleaned without its index, leaving names that point at nothing.</para>
    /// </remarks>
    public Task MarkPreparedAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                foreach (GalleryTile tile in _tiles)
                {
                    tile.IsPrepared = _thumbnails.Exists(tile.ThumbnailName);
                }
            },
            cancellationToken);
}

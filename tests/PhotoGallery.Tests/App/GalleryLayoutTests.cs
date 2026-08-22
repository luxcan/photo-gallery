using PhotoGallery.App.Gallery;

namespace PhotoGallery.Tests.App;

public sealed class GalleryLayoutTests
{
    private const double Default = GalleryLayout.DefaultCellSize;

    [Theory]
    [InlineData(1044, Default, 6)]
    [InlineData(208, 200, 1)]
    [InlineData(407, 200, 1)]   // one short of fitting a second 200px cell and its gap
    [InlineData(408, 200, 2)]   // exactly two cells and the gap between them
    [InlineData(1044, 200, 5)]
    [InlineData(1044, GalleryLayout.MaxCellSize, 3)]   // the same width, all the way in
    public void ColumnsFor_FitsWhatTheWidthAllows(double width, double cellSize, int expected) =>
        Assert.Equal(expected, GalleryLayout.ColumnsFor(width, cellSize));

    [Fact]
    public void ColumnsFor_NeverDropsBelowOne()
    {
        // A pane can be dragged narrower than a single cell, and a row of zero
        // pictures would divide by zero when the scroll position is worked out.
        Assert.Equal(1, GalleryLayout.ColumnsFor(40, Default));
        Assert.Equal(1, GalleryLayout.ColumnsFor(0, Default));
    }

    [Fact]
    public void RowHeight_IsTheCellPlusItsGap() =>
        Assert.Equal(Default + GalleryLayout.CellGap, GalleryLayout.RowHeight(Default));

    [Fact]
    public void FirstItemAt_NamesThePictureAtTheTopOfTheView()
    {
        // Three rows down, five to a row, is picture fifteen.
        Assert.Equal(15, GalleryLayout.FirstItemAt(GalleryLayout.RowHeight(Default) * 3, 5, Default));
        Assert.Equal(0, GalleryLayout.FirstItemAt(0, 5, Default));
    }

    [Fact]
    public void FirstItemAt_TreatsAPartlyScrolledRowAsThatRow() =>
        Assert.Equal(5, GalleryLayout.FirstItemAt(GalleryLayout.RowHeight(Default) * 1.9, 5, Default));

    [Fact]
    public void OffsetAndFirstItem_RoundTripSoAResizeKeepsThePictureInView()
    {
        // The point of the pair: re-flowing five columns into seven must leave
        // the same picture under the user's eye.
        int firstItem = GalleryLayout.FirstItemAt(GalleryLayout.RowHeight(Default) * 10, 5, Default);
        double offset = GalleryLayout.OffsetOf(firstItem, 7, Default);

        Assert.Equal(50, firstItem);
        Assert.Equal(49, GalleryLayout.FirstItemAt(offset, 7, Default), tolerance: 1);
    }

    [Fact]
    public void OffsetAndFirstItem_RoundTripWhenTheCellSizeChangesToo()
    {
        // A zoom moves the column count and the row height at once, which a
        // resize never does - the case where using one cell size for the
        // measurement and another for the restore would silently drift.
        const double zoomed = 300d;
        int firstItem = GalleryLayout.FirstItemAt(GalleryLayout.RowHeight(Default) * 10, 5, Default);
        int columns = GalleryLayout.ColumnsFor(1044, zoomed);
        double offset = GalleryLayout.OffsetOf(firstItem, columns, zoomed);

        // Landing on the start of the row that holds it is the promise, not the
        // exact index: the picture stays on screen.
        int restored = GalleryLayout.FirstItemAt(offset, columns, zoomed);
        Assert.InRange(restored, firstItem - columns, firstItem);
    }

    [Fact]
    public void OffsetOf_IsZeroForTheFirstPicture() =>
        Assert.Equal(0, GalleryLayout.OffsetOf(0, 5, Default));

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Normalise_FallsBackToTheDefaultForAnythingUnusable(double stored) =>
        Assert.Equal(Default, GalleryLayout.Normalise(stored));

    [Fact]
    public void Normalise_HoldsInsideTheStops()
    {
        // A database from another release, or a hand edit, must not produce a
        // cell the tiles cannot fill.
        Assert.Equal(GalleryLayout.MinCellSize, GalleryLayout.Normalise(50));
        Assert.Equal(GalleryLayout.MaxCellSize, GalleryLayout.Normalise(9999));
    }

    [Theory]
    [InlineData(220, 200)]   // nearer the stop below
    [InlineData(230, 250)]   // nearer the one above
    [InlineData(250, 250)]   // already a stop
    public void Normalise_SnapsToAStop(double stored, double expected) =>
        Assert.Equal(expected, GalleryLayout.Normalise(stored));

    [Fact]
    public void Zoomed_MovesOneStopPerNotch() =>
        Assert.Equal(Default + GalleryLayout.ZoomStep, GalleryLayout.Zoomed(Default, 120));

    [Fact]
    public void Zoomed_DoesNothingScrollingOutFromTheDefault()
    {
        // The default is also the floor: the grid opens as small as it goes, so
        // the first notch outwards is deliberately a no-op rather than a bug.
        Assert.Equal(Default, GalleryLayout.Zoomed(Default, -120));
        Assert.Equal(GalleryLayout.MinCellSize, Default);
    }

    [Fact]
    public void Zoomed_HoldsAtBothEndsRatherThanWrapping()
    {
        Assert.Equal(GalleryLayout.MaxCellSize, GalleryLayout.Zoomed(GalleryLayout.MaxCellSize, 120));
        Assert.Equal(GalleryLayout.MinCellSize, GalleryLayout.Zoomed(GalleryLayout.MinCellSize, -120));
    }

    [Fact]
    public void RowsOnScreen_CountsThePartRowsAtTopAndBottom()
    {
        // The grid scrolls by the pixel, not by the row, so at almost every
        // position there is part of a row showing at each end. Counting only the
        // whole ones left the last row of a maximised window grey.
        Assert.Equal(9, GalleryLayout.RowsOnScreen(1_280, Default));
        Assert.Equal(5, GalleryLayout.RowsOnScreen(4 * GalleryLayout.RowHeight(Default), Default));
    }

    [Fact]
    public void RowsOnScreen_IsNeverNoneHoweverSmallTheWindow()
    {
        Assert.Equal(1, GalleryLayout.RowsOnScreen(0, Default));
        Assert.Equal(1, GalleryLayout.RowsOnScreen(-50, Default));
    }

    [Fact]
    public void IsSamePlace_TreatsAWobbleSmallerThanARowAsAStandstill()
    {
        // What a virtualised list does at rest: WPF refines its guess at the
        // extent as rows realise, which nudges the position a few pictures at a
        // time and raises a scroll event for each nudge. Acting on those
        // restarted the wait for the viewport to settle, so it never finished.
        Assert.True(GalleryLayout.IsSamePlace(8_820, 8_825, columns: 21));
        Assert.True(GalleryLayout.IsSamePlace(8_820, 8_801, columns: 21));
    }

    [Fact]
    public void IsSamePlace_TreatsARowOrMoreAsARealMove()
    {
        Assert.False(GalleryLayout.IsSamePlace(8_820, 8_841, columns: 21));
        Assert.False(GalleryLayout.IsSamePlace(8_820, 8_799, columns: 21));
        Assert.False(GalleryLayout.IsSamePlace(0, 5_000, columns: 21));
    }

    [Fact]
    public void IsSamePlace_HasNoPreviousPlaceToBeTheSameAs()
    {
        // -1 is "nothing is in flight". A freshly loaded grid must never mistake
        // that for having already asked for the top of the library.
        Assert.False(GalleryLayout.IsSamePlace(-1, 0, columns: 21));
    }
}

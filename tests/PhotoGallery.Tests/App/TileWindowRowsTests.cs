using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Cutting tiles into the rows the list virtualises, once headings are in play.
/// </summary>
/// <remarks>
/// The row is the item WPF virtualises, and the flat list underneath it is what
/// the bitmap window indexes. Rows used to be uniformly full, so the scroll
/// position could be multiplied by the number across; restarting the chunking at
/// every heading breaks that, and a window pointed at the wrong pictures is a
/// screen of grey.
/// </remarks>
public sealed class TileWindowRowsTests
{
    [Fact]
    public void Rows_StartAfreshAtEachGroupSoNoRowStraddlesTwo()
    {
        TileWindow window = NewWindow(columns: 3);

        window.Fill([Group("Age 11", 4), Group("Age 10", 2)]);

        Assert.Equal([3, 1, 2], window.Rows.Select(row => row.Tiles.Count));
    }

    [Fact]
    public void Rows_CarryTheIndexOfTheirFirstTile()
    {
        // What replaced multiplying the row number by the number across. The
        // second group starts at 4 here, not at 6, because its first row is a
        // short one.
        TileWindow window = NewWindow(columns: 3);

        window.Fill([Group("Age 11", 4), Group("Age 10", 2)]);

        Assert.Equal([0, 3, 4], window.Rows.Select(row => row.FirstIndex));
    }

    [Fact]
    public void Rows_PutTheHeadingOnTheFirstRowOfItsGroupOnly()
    {
        TileWindow window = NewWindow(columns: 2);

        window.Fill([Group("Age 11", 4), Group("Age 10", 2)]);

        Assert.Equal(["Age 11", null, "Age 10"], window.Rows.Select(row => row.Heading));
        Assert.Equal([true, false, true], window.Rows.Select(row => row.HasHeading));
    }

    [Fact]
    public void Rows_OfAnUngroupedGridCarryNoHeadingAtAll()
    {
        // A library with no grouping is one group with no heading, so the two
        // screens stay on exactly one code path.
        TileWindow window = NewWindow(columns: 2);

        window.Fill(Tiles(3));

        Assert.All(window.Rows, row => Assert.False(row.HasHeading));
        Assert.Equal(3, window.Count);
    }

    [Fact]
    public void Rows_AreCutAgainForANewWidthWithoutLosingTheirTiles()
    {
        // A resize must not throw away decoded bitmaps, so the tiles are the
        // same objects on the other side of it.
        TileWindow window = NewWindow(columns: 3);
        window.Fill([Group("Age 11", 4)]);
        GalleryTile first = window[0];

        window.SetColumns(2);

        Assert.Equal([2, 2], window.Rows.Select(row => row.Tiles.Count));
        Assert.Same(first, window[0]);
        Assert.Equal([0, 2], window.Rows.Select(row => row.FirstIndex));
    }

    [Fact]
    public void Rows_FlattenInTheOrderTheGroupsWereGiven()
    {
        TileWindow window = NewWindow(columns: 10);

        window.Fill([Group("Age 11", 2), Group("Age 10", 2)]);

        Assert.Equal(4, window.Count);
        Assert.Equal(0, window.IndexOf(window[0]));
        Assert.Equal(3, window.IndexOf(window[3]));
    }

    private static TileWindow NewWindow(int columns)
    {
        var window = new TileWindow(new NoRenditionsOnDisk());
        window.SetColumns(columns);
        return window;
    }

    private static TileGroup Group(string heading, int count) =>
        new(heading, $"{count} pictures", null, [.. Tiles(count)]);

    private static IEnumerable<GalleryTile> Tiles(int count) =>
        Enumerable.Range(1, count).Select(id => new GalleryTile(new GalleryItem(
            id,
            $@"2020\{id}.jpg",
            $"{id}.jpg",
            "2020",
            $@"C:\one\2020\{id}.jpg",
            $"{id}",
            new DateTime(2020, 6, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2020, 6, 3, 12, 0, 0, DateTimeKind.Utc),
            0,
            AssetKind.Photo)));

    /// <summary>A store with nothing in it: these tests are about the chunking.</summary>
    private sealed class NoRenditionsOnDisk : IThumbnailStore
    {
        public Task<string> SaveAsync(GeneratedThumbnail thumbnail, CancellationToken token = default) =>
            Task.FromResult(string.Empty);

        public string NameFor(string contentHash) => contentHash;

        public string ResolveTilePath(string thumbnailName) => thumbnailName;

        public string ResolvePreviewPath(string thumbnailName) => thumbnailName;

        public bool Exists(string? thumbnailName) => false;

        public DateTime? PreviewWrittenUtc(string? thumbnailName) => null;

        public bool TryDelete(string? thumbnailName) => true;

        public IReadOnlyCollection<string> ListStoredNames() => [];

        public void RemoveEmptyShards()
        {
        }
    }
}

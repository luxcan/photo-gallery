using PhotoGallery.App.Gallery;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Another screen's grid with no question of its own: every photograph on it
/// stays, and nothing beside it needs putting right.
/// </summary>
/// <remarks>
/// For the tests about stepping and naming, where what matters is that the
/// viewer walks a grid other than the library's - not what that grid's screen
/// would say about a photograph changed on it.
/// </remarks>
internal sealed class PlainGrid : IViewerGrid
{
    public PlainGrid(TileWindow window) => Window = window;

    public TileWindow Window { get; }

    public Task<IReadOnlySet<int>> StillShowingAsync(IReadOnlyList<int> assetIds) =>
        Task.FromResult<IReadOnlySet<int>>(assetIds.ToHashSet());

    public void Dropped(GalleryTile tile)
    {
    }
}

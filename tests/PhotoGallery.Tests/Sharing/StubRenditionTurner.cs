using PhotoGallery.Application.Ports;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Stands in for the cached pictures, which a merge test has none of.
/// </summary>
/// <remarks>
/// It exists to answer one question honestly: whether there was a picture to
/// turn. That is the whole of what the merge does differently - a turn with a
/// rendition is applied, and a turn without one waits with the held answers
/// rather than being dropped - so a stub that always succeeds would leave the
/// interesting half untested.
/// </remarks>
internal sealed class StubRenditionTurner : IRenditionTurner
{
    private readonly Dictionary<string, TurnedRendition> _pictures = [];
    private readonly IThumbnailStore? _store;

    public StubRenditionTurner()
    {
    }

    /// <summary>
    /// Answers from a real thumbnail store as well as from what it was told.
    /// </summary>
    /// <remarks>
    /// For the tests where a picture arrives from the pool rather than being
    /// arranged: "was there a rendition to turn?" is then a question about the
    /// disk, and a stub that answered from its own dictionary would say no to a
    /// file that is sitting right there.
    /// </remarks>
    public StubRenditionTurner(IThumbnailStore store) => _store = store;

    /// <summary>Every turn asked for, in order, as name and degrees.</summary>
    public List<(string Name, int Degrees)> Turns { get; } = [];

    /// <summary>Says this rendition exists, at this size before any turn.</summary>
    public StubRenditionTurner Holding(string thumbnailName, int width = 1024, int height = 768)
    {
        _pictures[thumbnailName] = new TurnedRendition(width, height);
        return this;
    }

    public TurnedRendition? Turn(string thumbnailName, int degrees)
    {
        Turns.Add((thumbnailName, degrees));

        if (!_pictures.TryGetValue(thumbnailName, out TurnedRendition before))
        {
            if (_store?.Exists(thumbnailName) != true)
            {
                return null;
            }

            before = new TurnedRendition(1024, 768);
        }

        // The picture is now the other way round, which is what the next turn of
        // it would be measured against.
        _pictures[thumbnailName] = degrees % 180 == 0
            ? before
            : new TurnedRendition(before.Height, before.Width);

        return before;
    }
}

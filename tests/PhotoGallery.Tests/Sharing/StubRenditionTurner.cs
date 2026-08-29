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
            return null;
        }

        // The picture is now the other way round, which is what the next turn of
        // it would be measured against.
        _pictures[thumbnailName] = degrees % 180 == 0
            ? before
            : new TurnedRendition(before.Height, before.Width);

        return before;
    }
}

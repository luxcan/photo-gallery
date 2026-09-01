namespace PhotoGallery.Application.Ports;

/// <summary>
/// What happened when photographs were put into an album.
/// </summary>
/// <param name="Moved">
/// How many came out of another album to get here. The number the user
/// needs, because a photograph can only be in one place and nobody asked for
/// that rule.
/// </param>
/// <param name="From">
/// The albums they came out of, named, so the sentence on screen can say
/// where they went from rather than only that something moved.
/// </param>
public sealed record AlbumAddResult(int Added, int Moved, IReadOnlyList<string> From)
{
    public static AlbumAddResult Nothing { get; } = new(0, 0, []);
}

namespace PhotoGallery.App.Albums;

/// <summary>One line in the album panel's Collection list.</summary>
/// <remarks>
/// A select rather than a tick list, because an album is on one collection: the
/// two questions beside it in the panel admit any number of answers, and this
/// one admits exactly one, so it must not look like them.
/// </remarks>
/// <param name="Id">
/// Which collection, or zero for the line that takes the album off whichever it
/// was on. Zero rather than a null entry so the list is one type all the way
/// down and the selected item is never null while the panel is open.
/// </param>
public sealed record CollectionOption(int Id, string Name)
{
    /// <summary>What the list offers when the answer is "none of them".</summary>
    public static CollectionOption None { get; } = new(0, "Not on a collection");
}

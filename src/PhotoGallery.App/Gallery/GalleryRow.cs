namespace PhotoGallery.App.Gallery;

/// <summary>
/// A row of pictures, which is what the list actually virtualises.
/// </summary>
/// <remarks>
/// <para>
/// WPF has no virtualising wrap panel - the only virtualising panels it ships
/// are the stack panel and the two the data grid uses. Making a row the item
/// rather than a picture lets the stock <c>VirtualizingStackPanel</c> do the
/// work: roughly a dozen rows are alive at any moment whatever the library's
/// size, against 11,482 realised children and 90 MB of visual tree for a wrap
/// panel holding the same pictures.
/// </para>
/// <para>
/// A heading rides on the first row of its group rather than being a row of its
/// own. Two kinds of item in one list means two templates, and the list recycles
/// its containers - so a container built for a heading would sooner or later be
/// handed a row of pictures. Turning recycling off to allow it would cost more
/// than the headings are worth.
/// </para>
/// </remarks>
public sealed class GalleryRow
{
    public GalleryRow(IReadOnlyList<GalleryTile> tiles, int firstIndex)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        Tiles = tiles;
        FirstIndex = firstIndex;
    }

    public IReadOnlyList<GalleryTile> Tiles { get; }

    /// <summary>
    /// Where this row's first picture sits in the flat list the bitmap window
    /// indexes.
    /// </summary>
    /// <remarks>
    /// Rows used to be uniform, so the scroll position could be multiplied by
    /// the number across. Restarting the chunking at every heading breaks that,
    /// and a window pointed at the wrong pictures is a screen of grey.
    /// </remarks>
    public int FirstIndex { get; }

    /// <summary>The group heading, on the first row of a group only.</summary>
    public string? Heading { get; init; }

    /// <summary>The quieter half of the heading: the year, and how many.</summary>
    public string? HeadingDetail { get; init; }

    /// <summary>
    /// Said once per group, where every age in it was read off a file date
    /// rather than the camera's own.
    /// </summary>
    public string? HeadingNote { get; init; }

    public bool HasHeading => Heading is not null;
}

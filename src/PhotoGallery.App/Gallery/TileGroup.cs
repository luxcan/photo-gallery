namespace PhotoGallery.App.Gallery;

/// <summary>
/// A run of pictures shown under one heading, or the whole grid under none.
/// </summary>
/// <remarks>
/// The window chunks each group into rows separately, so a row never straddles
/// two headings. A grid with no grouping is one group with no heading, which
/// keeps the library and a person's page on exactly the same code path.
/// </remarks>
public sealed record TileGroup(
    string? Heading,
    string? Detail,
    string? Note,
    IReadOnlyList<GalleryTile> Tiles);

namespace PhotoGallery.Application.Ports;

/// <summary>What the gallery asked for, and how much there was altogether.</summary>
public sealed record GalleryPage(IReadOnlyList<GalleryItem> Items, int TotalCount);

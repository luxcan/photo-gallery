using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>
/// One run of somebody's pictures that share an age, in the order the reader
/// returned them.
/// </summary>
/// <param name="Bucket">
/// The age the group covers, or null when there is no year of birth to measure
/// from and the whole list is one unheaded run.
/// </param>
/// <param name="Heading">What the group is called, or null when it has no heading.</param>
/// <param name="DatedFromTheFile">
/// How many of these carry no capture date and were placed by their file date
/// instead. The screen says so rather than presenting an inferred age as a fact.
/// </param>
public sealed record AgeGroup(
    int? Bucket,
    string? Heading,
    int DatedFromTheFile,
    IReadOnlyList<GalleryItem> Photos)
{
    /// <summary>Whether every age in this group was read off a file date.</summary>
    public bool IsEntirelyInferred => Photos.Count > 0 && DatedFromTheFile == Photos.Count;
}

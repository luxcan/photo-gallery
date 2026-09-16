using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Albums;

/// <summary>One album as a line in the picker.</summary>
/// <param name="IsCurrent">
/// The one this photograph is in already. Marked rather than hidden: choosing
/// another moves it, and seeing where it is now is what makes that legible.
/// </param>
public sealed record AlbumChoice(AlbumSummary Summary, bool IsCurrent)
{
    public int Id => Summary.Id;

    public string Name => Summary.Name;

    public string Caption =>
        Summary.PhotoCount == 1 ? "1 photo" : $"{Summary.PhotoCount:N0} photos";

    /// <summary>
    /// The band this row starts, or empty for a row in the middle of one.
    /// </summary>
    /// <remarks>
    /// Carried on the row rather than drawn as two lists, which is how the wall
    /// already does its date headings: one list with a heading on the row that
    /// begins each run keeps the scrolling, the narrowing and the keyboard as
    /// one thing.
    /// </remarks>
    public string Heading { get; init; } = string.Empty;

    public bool HasHeading => Heading.Length > 0;
}

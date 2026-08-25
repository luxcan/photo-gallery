using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Collections;

/// <summary>One collection as a line in the picker.</summary>
/// <param name="IsCurrent">
/// The one this photograph is in already. Marked rather than hidden: choosing
/// another moves it, and seeing where it is now is what makes that legible.
/// </param>
public sealed record CollectionChoice(CollectionSummary Summary, bool IsCurrent)
{
    public int Id => Summary.Id;

    public string Name => Summary.Name;

    public string Caption =>
        Summary.PhotoCount == 1 ? "1 photo" : $"{Summary.PhotoCount:N0} photos";
}

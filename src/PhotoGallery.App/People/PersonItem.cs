using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.People;

/// <summary>Someone who has been named, as the list shows them.</summary>
public sealed partial class PersonItem : ObservableObject
{
    [ObservableProperty]
    private ImageSource? _picture;

    public PersonItem(PersonSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Summary = summary;
    }

    public PersonSummary Summary { get; private set; }

    /// <summary>
    /// Keeps the year just recorded, so the screen regroups without going back
    /// to the database for a number it was just given.
    /// </summary>
    public void RememberBirthYear(int? birthYear)
    {
        Summary = Summary with { BirthYear = birthYear };
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>
    /// Takes the counts from a fresh read of this person, keeping the row.
    /// </summary>
    /// <remarks>
    /// The row stays rather than being replaced, because it is the list's
    /// selected item: swapping it for a new one would read as somebody else
    /// being chosen, and its picture would have to be decoded again.
    /// </remarks>
    public void Recount(PersonSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Summary = summary;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(AwaitingReview));
        OnPropertyChanged(nameof(HasReview));
        OnPropertyChanged(nameof(Caption));
    }

    public int Id => Summary.Id;

    public string DisplayName => Summary.DisplayName;

    public int AwaitingReview => Summary.AwaitingReview;

    public bool HasReview => Summary.AwaitingReview > 0;

    public string Caption => Summary.Photos == 1
        ? "1 picture"
        : $"{Summary.Photos:N0} pictures";
}

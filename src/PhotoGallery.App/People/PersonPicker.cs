using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.People;

/// <summary>
/// Saying which person a face belongs to: pick a name, or type one that does not
/// exist yet.
/// </summary>
/// <remarks>
/// One of these serves both screens that ask the question. The photo viewer asks
/// it of a face nobody has named; the review screen asks it of a face the app
/// guessed wrong, where "no" and "no, it is her brother" are very different
/// answers and only the second one teaches anybody anything.
///
/// <para>What differs between the two is supplied by the caller: the wording,
/// and whether setting the face aside as nobody is on offer. What the face is
/// and what happens to the answer stay with the screen that opened it, so this
/// holds no assignment logic at all.</para>
/// </remarks>
public sealed partial class PersonPicker : ObservableObject
{
    private readonly Func<string, Task> _chosen;
    private readonly Func<Task>? _setAside;
    private readonly Action? _closed;

    private IReadOnlyList<PersonDirectoryEntry> _everyone = [];
    private int? _current;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _typed = string.Empty;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _hint = string.Empty;

    /// <param name="chosen">
    /// Told the name that was picked or typed. A name rather than an id, because
    /// somebody typed in is not anybody yet.
    /// </param>
    /// <param name="setAside">
    /// What "nobody" does, where the screen has an answer for it. Left out where
    /// it has none, and the button then does not appear.
    /// </param>
    /// <param name="closed">
    /// Told whenever the list goes away, however it goes away, so the screen can
    /// forget which face was being asked about. One place to do that rather than
    /// one per exit is what keeps Cancel, Escape and answering consistent.
    /// </param>
    public PersonPicker(
        Func<string, Task> chosen,
        Func<Task>? setAside = null,
        string setAsideLabel = "",
        Action? closed = null)
    {
        _chosen = chosen;
        _setAside = setAside;
        SetAsideLabel = setAsideLabel;
        _closed = closed;
    }

    public ObservableCollection<PersonChoiceItem> Choices { get; } = [];

    public string SetAsideLabel { get; }

    public bool CanSetAside => _setAside is not null && SetAsideLabel.Length > 0;

    /// <summary>
    /// True when what has been typed matches nobody, so the only thing left to
    /// do with it is add them.
    /// </summary>
    public bool HasNoMatch => Choices.Count == 0 && Typed.Trim().Length > 0;

    /// <param name="current">
    /// The name already on this face, so the list can show which one is current
    /// rather than making the user remember what they are changing.
    /// </param>
    public void Open(
        IReadOnlyList<PersonDirectoryEntry> everyone, int? current, string prompt, string hint)
    {
        _everyone = everyone ?? [];
        _current = current;
        Prompt = prompt;
        Hint = hint;

        // Set before narrowing rather than relying on the change to do it: it is
        // usually already empty, and an assignment that changes nothing raises
        // nothing.
        Typed = string.Empty;
        Narrow();

        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        Typed = string.Empty;
        Choices.Clear();
        _closed?.Invoke();
    }

    partial void OnTypedChanged(string value) => Narrow();

    /// <summary>
    /// The names on offer, cut down to what has been typed.
    /// </summary>
    /// <remarks>
    /// One box does both jobs. Typing narrows the list, and if nothing is left
    /// the same text is the new name - so a household of thirty is three
    /// keystrokes away rather than a scroll, and adding somebody is still the
    /// obvious thing to do when they are genuinely not there.
    /// </remarks>
    private void Narrow()
    {
        string typed = Typed.Trim();

        Choices.Clear();
        foreach (PersonDirectoryEntry person in _everyone)
        {
            if (typed.Length > 0
                && !person.DisplayName.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            Choices.Add(new PersonChoiceItem(person, person.Id == _current));
        }

        OnPropertyChanged(nameof(HasNoMatch));
    }

    [RelayCommand]
    private Task ChooseAsync(PersonChoiceItem? person) =>
        person is null ? Task.CompletedTask : _chosen(person.DisplayName);

    [RelayCommand]
    private Task AddAsync() =>
        string.IsNullOrWhiteSpace(Typed) ? Task.CompletedTask : _chosen(Typed.Trim());

    [RelayCommand]
    private Task SetAsideAsync() => _setAside?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private void Cancel() => Close();
}

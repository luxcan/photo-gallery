using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Albums;

/// <summary>
/// Choosing which album a photograph belongs in: pick one, or type a name
/// that does not exist yet.
/// </summary>
/// <remarks>
/// Deliberately the same shape as the picker that asks who a face is - one box
/// that narrows the list and, when nothing is left, becomes the new name. That
/// is a shape worth repeating rather than a copy worth avoiding: the two answer
/// about different things and share no logic beyond a filtered list.
///
/// <para>Two of these is the ceiling, though. A third means the pair should be
/// one type taking a list of names, rather than three near-identical files.</para>
/// </remarks>
public sealed partial class AlbumPicker : ObservableObject
{
    private readonly Func<string, Task> _chosen;
    private readonly Func<Task>? _removed;
    private readonly Action? _closed;

    private IReadOnlyList<AlbumSummary> _all = [];
    private int _current;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The album the photograph is in, or empty while it is in none.</summary>
    /// <remarks>
    /// Held as a name rather than read off the marked choice, because the way
    /// out has to be offered whatever has been typed - and typing three letters
    /// that the album's name does not contain takes it out of the list.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInOne), nameof(TakeOutLabel))]
    [NotifyCanExecuteChangedFor(nameof(TakeOutCommand))]
    private string _currentName = string.Empty;

    [ObservableProperty]
    private string _typed = string.Empty;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _hint = string.Empty;

    /// <param name="chosen">
    /// Told the name that was picked or typed. A name rather than an id, because
    /// an album typed in does not exist yet.
    /// </param>
    /// <param name="removed">
    /// Told to take the photograph out of the album it is in. Here rather than
    /// beside the picture because "no album" is one of the answers to "which
    /// album is this in", and a second button on the strip for it was a tick -
    /// which means confirm everywhere else in this app.
    /// </param>
    /// <param name="closed">
    /// Told whenever the list goes away, however it goes away, so one place
    /// forgets what was being asked rather than one per exit.
    /// </param>
    public AlbumPicker(
        Func<string, Task> chosen, Func<Task>? removed = null, Action? closed = null)
    {
        _chosen = chosen;
        _removed = removed;
        _closed = closed;
    }

    public ObservableCollection<AlbumChoice> Choices { get; } = [];

    /// <summary>
    /// True when what has been typed matches nothing, so the only thing left to
    /// do with it is make it.
    /// </summary>
    public bool HasNoMatch => Choices.Count == 0 && Typed.Trim().Length > 0;

    /// <summary>
    /// How much of the list the typing is hiding.
    /// </summary>
    /// <remarks>
    /// Empty until something is typed. A filtered list and a short list look
    /// identical, and the moment there are more albums than fit the panel, "3"
    /// with no denominator reads as "you have three albums".
    /// </remarks>
    public string Narrowed => Typed.Trim().Length == 0
        ? string.Empty
        : $"{Choices.Count:N0} of {_all.Count:N0}";

    /// <summary>Whether there is a count to show.</summary>
    public bool IsNarrowed => Narrowed.Length > 0;

    /// <summary>Whether there is an album to be taken out of.</summary>
    public bool IsInOne => CurrentName.Length > 0;

    /// <summary>Named, so the way out cannot be pressed without reading which.</summary>
    public string TakeOutLabel => $"Take it out of {CurrentName}";

    /// <param name="current">
    /// The album this photograph is already in, so the list can mark it -
    /// putting it somewhere else moves it, and the user should be able to see
    /// what they are moving it out of.
    /// </param>
    public void Open(IReadOnlyList<AlbumSummary> all, int current, string prompt, string hint)
    {
        _all = all ?? [];
        _current = current;
        Prompt = prompt;
        Hint = hint;

        CurrentName = _all.FirstOrDefault(album => album.Id == current)?.Name
            ?? string.Empty;

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

    private void Narrow()
    {
        string typed = Typed.Trim();

        Choices.Clear();
        foreach (AlbumSummary album in _all)
        {
            if (typed.Length > 0
                && !album.Name.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            Choices.Add(new AlbumChoice(album, album.Id == _current));
        }

        OnPropertyChanged(nameof(HasNoMatch));
        OnPropertyChanged(nameof(Narrowed));
        OnPropertyChanged(nameof(IsNarrowed));
    }

    [RelayCommand]
    private Task ChooseAsync(AlbumChoice? album) =>
        album is null ? Task.CompletedTask : _chosen(album.Name);

    [RelayCommand]
    private Task AddAsync() =>
        string.IsNullOrWhiteSpace(Typed) ? Task.CompletedTask : _chosen(Typed.Trim());

    /// <summary>Answers "none of them" - the photograph belongs in no album.</summary>
    [RelayCommand(CanExecute = nameof(IsInOne))]
    private Task TakeOutAsync() => _removed?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private void Cancel() => Close();
}

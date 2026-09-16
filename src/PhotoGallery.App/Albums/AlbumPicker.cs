using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;

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

    /// <summary>
    /// How many of your own albums stand under the RECENT heading.
    /// </summary>
    /// <remarks>
    /// Four rather than three: the list is about 300 DIP tall and shows seven,
    /// so four leaves the heading under it on screen. A band that fills the fold
    /// is a band nobody learns there is anything below.
    /// </remarks>
    private const int RecentCount = 4;

    private IReadOnlyList<AlbumSummary> _all = [];
    private int _yours;
    private int _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenBesideTheBar), nameof(IsOpenInTheMiddle))]
    private bool _isOpen;

    /// <summary>
    /// Whether this is drawn on the control that asked, rather than centred in
    /// the middle of the window over a shade.
    /// </summary>
    /// <remarks>
    /// A selection's question belongs beside the bar it was asked from. The
    /// photographs being filed are ticked on the wall directly behind it, and a
    /// centred panel over a shade hides the one thing worth looking at while
    /// answering - which album these belong in is a question about them.
    ///
    /// <para>One photograph in the viewer is the other case and keeps the modal:
    /// there the picture is already the whole screen, and a panel rising from a
    /// button in the side strip would have less to anchor to than the middle.</para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenBesideTheBar), nameof(IsOpenInTheMiddle))]
    private bool _isAnchored;

    /// <summary>Open, and drawn on the bar that asked.</summary>
    public bool IsOpenBesideTheBar => IsOpen && IsAnchored;

    /// <summary>Open, and drawn in the middle, over a shade.</summary>
    public bool IsOpenInTheMiddle => IsOpen && !IsAnchored;

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

    /// <summary>What the Make button promises to do with what is typed.</summary>
    [ObservableProperty]
    private string _makeTip = DefaultMakeTip;

    private const string DefaultMakeTip =
        "Make an album with this name and put the photograph in it";

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
    /// <param name="makeTip">
    /// What the Make button promises, for the one thing that differs between
    /// asking about one photograph and asking about forty.
    /// </param>
    /// <param name="anchored">
    /// Whether to draw beside whatever asked rather than in the middle. See
    /// <see cref="IsAnchored"/>.
    /// </param>
    public void Open(
        IReadOnlyList<AlbumSummary> all,
        int current,
        string prompt,
        string hint,
        string? makeTip = null,
        bool anchored = false)
    {
        _all = Ordered(all ?? []);
        _yours = _all.Count(album => album.Origin != AlbumOrigin.Proposed);
        _current = current;
        Prompt = prompt;
        Hint = hint;
        MakeTip = makeTip ?? DefaultMakeTip;
        IsAnchored = anchored;

        CurrentName = _all.FirstOrDefault(album => album.Id == current)?.Name
            ?? string.Empty;

        Typed = string.Empty;
        Narrow();

        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    /// <summary>
    /// Everything that has to happen however the list goes away.
    /// </summary>
    /// <remarks>
    /// On the property rather than in <see cref="Close"/>, because closing is no
    /// longer only something this class does: a popover dismisses itself when
    /// the pointer goes elsewhere, and it does that by writing IsOpen. Left in
    /// Close, a panel clicked away would leave what was being asked about still
    /// spoken for, and the typing from last time waiting in the box.
    /// </remarks>
    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            return;
        }

        Typed = string.Empty;
        Choices.Clear();
        _closed?.Invoke();
    }

    partial void OnTypedChanged(string value) => Narrow();

    /// <summary>
    /// Your own albums first, the one you filed into most recently at the top.
    /// </summary>
    /// <remarks>
    /// The list arrives in the order the albums wall wants - by the span of the
    /// photographs in them - which for filing is no order at all: a 2019 holiday
    /// you added to last night sits near the bottom of three hundred.
    ///
    /// <para>Recency is measured over albums you made or kept, and only those,
    /// which is the whole point. A rebuild writes every proposal's memberships
    /// in one go: on the first real library 284 of 291 albums share a last-added
    /// moment to the millisecond, so ordering everything by it would put
    /// whatever the last scan happened to touch at the top and call it recent.
    /// The seven albums a person actually made are the seven they file into.</para>
    ///
    /// <para><see cref="Enumerable.OrderByDescending{T, TKey}(IEnumerable{T},
    /// Func{T, TKey})"/> is stable, so an album nobody has put anything in keeps
    /// the place it arrived in rather than being shuffled against its
    /// equals.</para>
    /// </remarks>
    private static IReadOnlyList<AlbumSummary> Ordered(IReadOnlyList<AlbumSummary> all)
    {
        List<AlbumSummary> yours = [];
        List<AlbumSummary> rest = [];

        foreach (AlbumSummary album in all)
        {
            (album.Origin == AlbumOrigin.Proposed ? rest : yours).Add(album);
        }

        return [.. yours.OrderByDescending(album => album.LastAddedUtc ?? DateTime.MinValue), .. rest];
    }

    private void Narrow()
    {
        string typed = Typed.Trim();

        // Headings only over the whole list. On a narrowed one they would band
        // three rows into two, and "recent" is not what somebody who has typed
        // four letters is looking at the list for.
        int split = Math.Min(RecentCount, _yours);
        bool banded = typed.Length == 0 && _yours > 0 && _all.Count > split;

        Choices.Clear();
        foreach (AlbumSummary album in _all)
        {
            if (typed.Length > 0
                && !album.Name.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            string heading = banded switch
            {
                true when Choices.Count == 0 => "RECENT",
                true when Choices.Count == split => "EVERYTHING ELSE",
                _ => string.Empty,
            };

            Choices.Add(new AlbumChoice(album, album.Id == _current) { Heading = heading });
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

using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoGallery.App.Imaging;
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
    private readonly IThumbnailStore? _thumbnails;

    /// <summary>How wide each cover is decoded, for rows that draw them at 36.</summary>
    /// <remarks>
    /// A little over twice the drawn size, so a high-density display has pixels
    /// to spend and neither orientation is stretched to fill the square.
    /// </remarks>
    private const int CoverWidth = 80;

    /// <summary>How many covers are read at once.</summary>
    /// <remarks>
    /// The albums wall reads its own the same way and at the same width of
    /// parallelism. More threads than this spend longer contending for the disk
    /// than they save.
    /// </remarks>
    private const int CoverReaders = 4;

    /// <summary>
    /// The cover for each album, by album id, once it has been read.
    /// </summary>
    /// <remarks>
    /// Held here rather than on the rows, because the rows do not live long
    /// enough to hold anything: <see cref="Narrow"/> rebuilds every one of them
    /// on every keystroke, so a picture kept on a row would be thrown away and
    /// read again once per letter typed.
    /// </remarks>
    private readonly Dictionary<int, ImageSource?> _covers = [];

    /// <summary>
    /// Which opening of the list the covers being read belong to.
    /// </summary>
    /// <remarks>
    /// Reading three hundred of them outlives a panel somebody opens and shuts
    /// again, and the answer arriving afterwards would rebuild a list that has
    /// moved on - over whatever the next question had typed into it.
    /// </remarks>
    private int _opening;

    /// <summary>
    /// How many albums the list offers before anything is typed.
    /// </summary>
    /// <remarks>
    /// The recent few, and nothing else: with the app's own suggestions gone
    /// there is no long tail worth scrolling, and a second band under the first
    /// only said "everything else" over three more rows. Anything older is
    /// found by typing, which is what the box is for.
    ///
    /// <para>Eight because the list is about 300 DIP tall and shows six or
    /// seven: enough that a library of this size shows all of its albums and
    /// says nothing about more, and still a bound on one that grows.</para>
    /// </remarks>
    private const int RecentCount = 8;

    private IReadOnlyList<AlbumSummary> _all = [];
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
    [NotifyPropertyChangedFor(nameof(CanCreate), nameof(CreateLabel))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _typed = string.Empty;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _hint = string.Empty;

    /// <summary>What Create promises to do with what has been typed.</summary>
    [ObservableProperty]
    private string _createTip = DefaultCreateTip;

    private const string DefaultCreateTip =
        "Create an album with this name and put the photograph in it";

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
    /// <param name="thumbnails">
    /// Where the covers are read from. Left out where there are none to show -
    /// the rows are then names and counts, which is all they were.
    /// </param>
    public AlbumPicker(
        Func<string, Task> chosen,
        Func<Task>? removed = null,
        Action? closed = null,
        IThumbnailStore? thumbnails = null)
    {
        _chosen = chosen;
        _removed = removed;
        _closed = closed;
        _thumbnails = thumbnails;
    }

    public ObservableCollection<AlbumChoice> Choices { get; } = [];

    /// <summary>
    /// True when what has been typed matches nothing, so the only thing left to
    /// do with it is make it.
    /// </summary>
    public bool HasNoMatch => Choices.Count == 0 && Typed.Trim().Length > 0;

    /// <summary>
    /// Whether what has been typed is a name no album already carries.
    /// </summary>
    /// <remarks>
    /// The box is a search box, so creating is what is offered when the search
    /// does not turn up the album somebody wanted. The offer used to be a button
    /// standing beside the box at all times, which is what made the box read as
    /// the way to name a new album rather than the way to find an old one.
    ///
    /// <para>Exact rather than "nothing matched", and the distinction matters.
    /// The box narrows on any part of a name, so typing "Bali" beside an album
    /// called "Bali 2019" does find something - and an offer that went away
    /// whenever anything matched would make the name "Bali" impossible to give
    /// at all. Only a name already taken outright has nothing left to
    /// create.</para>
    /// </remarks>
    public bool CanCreate => Typed.Trim().Length > 0
        && !_all.Any(album => string.Equals(
            album.Name, Typed.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Named after what it would create, so it cannot be pressed blind.</summary>
    public string CreateLabel => $"Create “{Typed.Trim()}”";

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
        _current = current;
        Prompt = prompt;
        Hint = hint;
        CreateTip = makeTip ?? DefaultCreateTip;
        IsAnchored = anchored;

        // Read off the whole list rather than the offered one, so that a
        // photograph sitting in a suggestion still says which, and can still be
        // taken out of it.
        CurrentName = (all ?? []).FirstOrDefault(album => album.Id == current)?.Name
            ?? string.Empty;

        _all = Offerable(all ?? [], current);

        Typed = string.Empty;
        _covers.Clear();
        Narrow();

        IsOpen = true;

        // Started rather than awaited: the list is readable the moment it is on
        // screen, and the pictures land on it as they arrive. Waiting for three
        // hundred of them would mean a panel that opens a third of a second
        // after it was asked for.
        _ = ReadCoversAsync(_all, ++_opening);
    }

    /// <summary>
    /// Reads every album's cover, small, off the UI thread.
    /// </summary>
    /// <remarks>
    /// One rebuild when they are all in rather than three hundred as they
    /// arrive. The list ends up the same either way a moment later, and a
    /// collection that changes three hundred times is three hundred re-layouts
    /// of a panel nobody has finished reading.
    /// </remarks>
    private async Task ReadCoversAsync(IReadOnlyList<AlbumSummary> albums, int opening)
    {
        if (_thumbnails is not IThumbnailStore store || albums.Count == 0)
        {
            return;
        }

        // Written into a slot each rather than into the dictionary, because the
        // dictionary is the UI thread's and this is not.
        var read = new (int Id, ImageSource? Cover)[albums.Count];

        await Task.Run(() => Parallel.For(
            0,
            albums.Count,
            new ParallelOptions { MaxDegreeOfParallelism = CoverReaders },
            at => read[at] = (
                albums[at].Id,
                TileImageLoader.LoadThumb(store, albums[at].CoverThumbnailName, CoverWidth))))
            .ConfigureAwait(true);

        // The panel this was read for may have been shut and another opened
        // while it ran, and putting these on that one would rebuild a list that
        // has moved on - over whatever the next question had typed into it.
        if (opening != _opening || !IsOpen)
        {
            return;
        }

        foreach ((int id, ImageSource? cover) in read)
        {
            _covers[id] = cover;
        }

        Narrow();
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
    /// The albums worth offering, the one filed into most recently first.
    /// </summary>
    /// <remarks>
    /// Albums you made or kept, and not the app's own suggestions. Three
    /// reasons, and the first is not about tidiness at all.
    ///
    /// <para>A rebuild may change or remove a proposal - that is what makes it a
    /// proposal rather than an album - and putting a photograph in one does not
    /// make it yours. So filing into a suggestion can be quietly undone by the
    /// next scan, which is the one thing filing must never do.</para>
    ///
    /// <para>They are also nearly all of them - 284 of 291 on the first real
    /// library - and they are named after the places and dates they were grouped
    /// from, so searching for an album turned up a list of locations. Typing
    /// "tam" found forty-three, of which four were called "Tampines Estate".</para>
    ///
    /// <para>The one kept whatever it is, is the album this photograph is in
    /// already: it carries the tick saying where the picture is now, and the way
    /// out of it. A suggestion worth filing into is kept on the Albums screen,
    /// and once kept it is here.</para>
    ///
    /// <para>The order the list arrives in is the albums wall's - by the span of
    /// the photographs in them - which for filing is no order at all: a 2019
    /// holiday added to last night sits near the bottom.
    /// <see cref="Enumerable.OrderByDescending{T, TKey}(IEnumerable{T},
    /// Func{T, TKey})"/> is stable, so an album nobody has put anything in keeps
    /// the place it arrived in rather than being shuffled against its
    /// equals.</para>
    /// </remarks>
    private static IReadOnlyList<AlbumSummary> Offerable(
        IReadOnlyList<AlbumSummary> all, int current) =>
    [
        .. all
            .Where(album => album.Origin != AlbumOrigin.Proposed || album.Id == current)
            .OrderByDescending(album => album.LastUsedUtc ?? DateTime.MinValue),
    ];

    private void Narrow()
    {
        string typed = Typed.Trim();

        // Untouched, the list is the recent few. Typed into, it is every album
        // that matches - or an older one could never be reached at all.
        List<AlbumSummary> showing = typed.Length == 0
            ? [.. _all.Take(RecentCount)]
            : [.. _all.Where(album =>
                album.Name.Contains(typed, StringComparison.CurrentCultureIgnoreCase))];

        Hidden = typed.Length == 0 ? _all.Count - showing.Count : 0;

        Choices.Clear();
        foreach (AlbumSummary album in showing)
        {
            Choices.Add(new AlbumChoice(album, album.Id == _current)
            {
                // On the first row only, and only over the untouched list: it
                // says what the order is, and on a narrowed list "recent" is not
                // what somebody who has typed four letters is looking at.
                Heading = typed.Length == 0 && Choices.Count == 0 ? "RECENT" : string.Empty,
                Cover = _covers.GetValueOrDefault(album.Id),
            });
        }

        OnPropertyChanged(nameof(HasNoMatch));
        OnPropertyChanged(nameof(Narrowed));
        OnPropertyChanged(nameof(IsNarrowed));
    }

    /// <summary>
    /// How many albums the untouched list is not showing.
    /// </summary>
    /// <remarks>
    /// Said rather than left to be discovered. A list that quietly stops at
    /// eight is a list somebody concludes their album has gone from.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore), nameof(MoreCaption))]
    private int _hidden;

    public bool HasMore => Hidden > 0;

    public string MoreCaption => Hidden == 1
        ? "1 more album. Type to find it."
        : $"{Hidden:N0} more albums. Type to find one.";

    [RelayCommand]
    private Task ChooseAsync(AlbumChoice? album) =>
        album is null ? Task.CompletedTask : _chosen(album.Name);

    /// <summary>Makes the album nobody could find, and files into it.</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private Task CreateAsync() =>
        string.IsNullOrWhiteSpace(Typed) ? Task.CompletedTask : _chosen(Typed.Trim());

    /// <summary>Answers "none of them" - the photograph belongs in no album.</summary>
    [RelayCommand(CanExecute = nameof(IsInOne))]
    private Task TakeOutAsync() => _removed?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private void Cancel() => Close();
}

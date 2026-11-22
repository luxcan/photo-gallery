using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PhotoGallery.App.Gallery;

/// <summary>
/// The photographs ticked on one wall, and the mode that ticks them.
/// </summary>
/// <remarks>
/// One of these per wall that can choose several - the library's and an open
/// album's - so that what a click means, how a Shift run is measured and what
/// the bar says are one rule rather than two copies that have to agree. It
/// knows nothing about what is done with the photographs once chosen: filing
/// and deleting belong to the screen, and this only answers which ones.
/// </remarks>
public sealed partial class PhotoChoice : ObservableObject
{
    private readonly TileWindow _window;

    /// <summary>
    /// The photographs ticked on the wall, by the id everything downstream
    /// wants.
    /// </summary>
    /// <remarks>
    /// Held here rather than read off the tiles, because a tile does not live
    /// long enough to hold it: <see cref="TileWindow.Fill(IEnumerable{GalleryTile})"/>
    /// replaces every one of them whenever the wall is read again - a search, a
    /// folder, a reload after a deletion. <see cref="GalleryTile.IsChosen"/> is
    /// the mirror the wall draws, put back from this set whenever tiles are built.
    ///
    /// <para>Ids rather than tiles for the same reason, and because they are
    /// already what everything done with a choice takes: a list of asset ids.</para>
    /// </remarks>
    private readonly HashSet<int> _chosen = [];

    /// <summary>
    /// The photograph a run taken with Shift is measured from.
    /// </summary>
    /// <remarks>
    /// The tile itself, never its position. The window hands out fresh indices
    /// on every rebuild - a resize alone changes how many sit in a row - so a
    /// number kept between two clicks would pick out a different run than the
    /// one under the pointer.
    /// </remarks>
    private GalleryTile? _anchor;

    public PhotoChoice(TileWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    /// <summary>
    /// Whether clicking a photograph ticks it rather than opening it.
    /// </summary>
    /// <remarks>
    /// A mode rather than a modifier. Clicking a picture to see it large is a
    /// settled rule of every wall, so the way to choose several cannot be a
    /// different click on the same tile - and a modifier nothing on screen
    /// mentions is a feature only the person who wrote it knows about.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasChosen),
        nameof(ChosenSummary),
        nameof(DeleteChosenCaption),
        nameof(EverythingChosen))]
    private bool _isChoosing;

    /// <summary>The ticked photographs, as a list of their own.</summary>
    /// <remarks>
    /// A copy, so a write that takes a while is not reading a set that a click
    /// can still change underneath it.
    /// </remarks>
    public IReadOnlyList<int> Ids => [.. _chosen];

    public int ChosenCount => _chosen.Count;

    public bool HasChosen => _chosen.Count > 0;

    /// <summary>
    /// Whether everything on the wall is already ticked, so Select all has
    /// nothing left to do.
    /// </summary>
    /// <remarks>
    /// Read by the bar to switch Select all off once it would do nothing. A
    /// control that stays lit and then does nothing when pressed is how somebody
    /// concludes there is no way out of a selection - which is exactly what
    /// happened: the obvious gesture after Select all is to press it again, and
    /// it was a silent no-op. Switched off, the eye goes to Select none instead,
    /// which is the control that does have something to do.
    /// </remarks>
    public bool EverythingChosen => _chosen.Count > 0 && _chosen.Count == _window.Tiles.Count;

    /// <summary>Whether Select all would change anything.</summary>
    public bool CanChooseEverything => _window.Tiles.Count > 0 && !EverythingChosen;

    /// <summary>How many are ticked, said in the bar beside the tick.</summary>
    public string ChosenSummary => _chosen.Count switch
    {
        0 => "None chosen",
        1 => "1 chosen",
        _ => $"{_chosen.Count:N0} chosen",
    };

    /// <summary>
    /// What the delete button says, with the count in it.
    /// </summary>
    /// <remarks>
    /// The same shape the duplicates screen's own delete uses. A button that
    /// says how many it is about to destroy is the last place the number can be
    /// noticed before the question is asked.
    /// </remarks>
    public string DeleteChosenCaption => _chosen.Count == 1
        ? "Delete 1 photo"
        : $"Delete {_chosen.Count:N0} photos";

    /// <summary>Whether this photograph is ticked, for a tile being built.</summary>
    public bool Contains(int assetId) => _chosen.Contains(assetId);

    /// <summary>Ticks a photograph, or unticks one already ticked.</summary>
    public void Toggle(GalleryTile? tile)
    {
        if (tile is null)
        {
            return;
        }

        if (!_chosen.Remove(tile.Item.Id))
        {
            _chosen.Add(tile.Item.Id);
        }

        tile.IsChosen = _chosen.Contains(tile.Item.Id);
        _anchor = tile;
        NotifyChosen();
    }

    /// <summary>
    /// Ticks a photograph from the circle drawn on it, starting the mode if it
    /// is not already on.
    /// </summary>
    /// <remarks>
    /// The way in as well as the way to add one more. Choosing used to begin at
    /// a pill at the far end of the strip, and a mode you must find a button for
    /// before any picture will answer is a step nobody guesses at - it was
    /// reported as the app having no way to choose several at all. The circle on
    /// the tile means both things at once.
    ///
    /// <para>Deliberately not the job of a click on the picture. That is the
    /// rule about what a click on a <em>picture</em> means, and it must go on
    /// meaning "show me this one" while the mode is off.</para>
    /// </remarks>
    [RelayCommand]
    private void ChoosePhoto(GalleryTile? tile) => ChoosePhoto(tile, extend: false);

    /// <summary>
    /// The same, and with Shift held, everything from the last one ticked to
    /// this one.
    /// </summary>
    public void ChoosePhoto(GalleryTile? tile, bool extend)
    {
        if (tile is null)
        {
            return;
        }

        IsChoosing = true;

        if (extend && _anchor is not null && !ReferenceEquals(_anchor, tile))
        {
            TickRun(_anchor, tile);
            return;
        }

        Toggle(tile);
    }

    /// <summary>Ticks every photograph between two of them, both ends included.</summary>
    /// <remarks>
    /// Adds rather than toggles. A run is what somebody means to have chosen, so
    /// one already ticked inside it stays ticked rather than being switched off
    /// by the gesture that was meant to include it.
    ///
    /// <para>The anchor stays where it was, so a second Shift-click stretches
    /// the same run rather than starting a new one from wherever the last
    /// ended - which is what every other list on this machine does.</para>
    /// </remarks>
    private void TickRun(GalleryTile from, GalleryTile to)
    {
        int first = _window.IndexOf(from);
        int last = _window.IndexOf(to);

        // A tile the wall no longer holds - a reload between the two clicks -
        // has no run to measure from, so the second click is simply a tick.
        if (first < 0 || last < 0)
        {
            Toggle(to);
            return;
        }

        if (first > last)
        {
            (first, last) = (last, first);
        }

        for (int at = first; at <= last; at++)
        {
            GalleryTile tile = _window[at];

            _chosen.Add(tile.Item.Id);
            tile.IsChosen = true;
        }

        NotifyChosen();
    }

    /// <summary>
    /// Ticks everything the wall is showing.
    /// </summary>
    /// <remarks>
    /// Every tile, not every visible one: the whole wall is in memory - only
    /// the pictures follow the viewport - so "all" can honestly mean all of
    /// what is on this wall. What is filtered out was never on it.
    /// </remarks>
    [RelayCommand]
    private void ChooseEverything()
    {
        foreach (GalleryTile tile in _window.Tiles)
        {
            _chosen.Add(tile.Item.Id);
            tile.IsChosen = true;
        }

        NotifyChosen();
    }

    /// <summary>Unticks everything, leaving the mode on.</summary>
    [RelayCommand]
    private void ChooseNothing() => Forget();

    /// <summary>
    /// Leaves the mode, which is also what Escape does.
    /// </summary>
    /// <remarks>
    /// Switching off forgets what was ticked. Keeping it would mean a wall that
    /// looks untouched and still has forty photographs spoken for, and the next
    /// press of Delete would be about pictures nobody could see were chosen.
    /// </remarks>
    [RelayCommand]
    public void StopChoosing()
    {
        Forget();
        IsChoosing = false;
    }

    /// <summary>Lets go of one photograph the wall has stopped showing.</summary>
    public void Forget(int assetId)
    {
        if (_chosen.Remove(assetId))
        {
            NotifyChosen();
        }
    }

    /// <summary>
    /// Drops anything ticked that the wall, just read again, no longer shows.
    /// </summary>
    /// <remarks>
    /// Acting on a photograph the wall has stopped showing would be acting on
    /// something nobody can see.
    /// </remarks>
    public void KeepOnlyWhatIsShowing()
    {
        _chosen.IntersectWith(_window.Tiles.Select(tile => tile.Item.Id));
        NotifyChosen();
    }

    private void Forget()
    {
        _chosen.Clear();
        _anchor = null;

        foreach (GalleryTile tile in _window.Tiles)
        {
            tile.IsChosen = false;
        }

        NotifyChosen();
    }

    private void NotifyChosen()
    {
        OnPropertyChanged(nameof(Ids));
        OnPropertyChanged(nameof(ChosenCount));
        OnPropertyChanged(nameof(HasChosen));
        OnPropertyChanged(nameof(ChosenSummary));
        OnPropertyChanged(nameof(DeleteChosenCaption));
        OnPropertyChanged(nameof(EverythingChosen));
        OnPropertyChanged(nameof(CanChooseEverything));
    }
}

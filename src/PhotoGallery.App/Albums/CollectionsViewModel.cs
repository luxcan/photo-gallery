using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Imaging;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;

namespace PhotoGallery.App.Albums;

/// <summary>
/// The shelves above the albums: the band across the top of the screen, and the
/// one that is open.
/// </summary>
/// <remarks>
/// A part of the albums screen rather than a screen of its own, and its own type
/// rather than more of <see cref="AlbumsViewModel"/>, which already carries two
/// tabs, an open album, a rule editor and two panels. What it owns is the band,
/// which shelf is open, and the three writes a shelf allows - make one, name it,
/// say what is on it. What it does not own is the wall: which albums are drawn
/// is a question about albums, and the answer stays where the albums are.
///
/// <para>Everything it writes is announced through <see cref="Changed"/> rather
/// than acted on directly, so there is one place that re-reads the library and
/// one status line on the screen.</para>
/// </remarks>
public sealed partial class CollectionsViewModel : ObservableObject
{
    /// <summary>How many covers are decoded at once, as elsewhere.</summary>
    private const int DecodeParallelism = 4;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThumbnailStore _store;

    /// <summary>
    /// The collection being named, or zero while the name is for a new one.
    /// </summary>
    /// <remarks>
    /// One panel for both, because naming a shelf and renaming it ask the same
    /// question and refuse the same answers. Two panels would be two copies of
    /// the rule that a name cannot be blank and cannot already be taken.
    /// </remarks>
    private int _naming;

    /// <summary>Which shelves exist, for reading an album's column against.</summary>
    private HashSet<int> _known = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpen), nameof(OpenName))]
    [NotifyCanExecuteChangedFor(nameof(StartRenamingCommand), nameof(StartPickingCommand),
        nameof(SavePickCommand), nameof(DeleteCommand))]
    private CollectionItem? _open;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NamingTitle))]
    private bool _isNaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameProblem), nameof(HasNameProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveNameCommand))]
    private string _typedName = string.Empty;

    [ObservableProperty]
    private bool _isPicking;

    /// <summary>
    /// True while this is reading or writing.
    /// </summary>
    /// <remarks>
    /// Every command that writes is listed, for the reason the albums screen
    /// lists its own: a button realised while the screen happens to be busy
    /// evaluates CanExecute once and stays dead unless something tells it to ask
    /// again.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(SaveNameCommand), nameof(StartPickingCommand),
        nameof(SavePickCommand), nameof(DeleteCommand), nameof(StartRenamingCommand))]
    private bool _isBusy;

    public CollectionsViewModel(IServiceScopeFactory scopeFactory, IThumbnailStore store)
    {
        _scopeFactory = scopeFactory;
        _store = store;
    }

    /// <summary>
    /// Raised after anything here is written, carrying what to say about it.
    /// </summary>
    /// <remarks>
    /// The albums screen listens, because every write here changes which albums
    /// the wall should be drawing - and an empty sentence is a real answer,
    /// meaning the library changed and there is nothing worth saying.
    /// </remarks>
    public event EventHandler<string>? Changed;

    /// <summary>Every collection, by name, which is the order the band shows.</summary>
    public ObservableCollection<CollectionItem> All { get; } = [];

    /// <summary>The albums offered when filling the open shelf.</summary>
    public ObservableCollection<TickChoice> Choices { get; } = [];

    /// <summary>
    /// Whether the band is worth drawing at all.
    /// </summary>
    /// <remarks>
    /// A library that never makes a collection sees the screen it saw before
    /// this existed, rather than an empty strip explaining a feature it is not
    /// using.
    /// </remarks>
    public bool HasAny => All.Count > 0;

    /// <summary>
    /// How many shelves there are, beside the band's heading.
    /// </summary>
    /// <remarks>
    /// A count rather than a repeat of the word, because the band scrolls
    /// sideways: what is off the right edge is the one thing the heading can
    /// say that the row cannot.
    /// </remarks>
    public string ShelfCount => All.Count == 1 ? "1 shelf" : $"{All.Count:N0} shelves";

    public bool HasOpen => Open is not null;

    public string OpenName => Open?.Name ?? string.Empty;

    public bool IsIdle => !IsBusy;

    /// <summary>Which shelves exist, for reading an album's column against.</summary>
    /// <remarks>
    /// The wall treats a shelf it has never heard of as no shelf. There is no
    /// foreign key on that column - see AlbumConfiguration for why - so an album
    /// left pointing at a collection that is gone would otherwise be an album
    /// that appears nowhere at all.
    /// </remarks>
    public IReadOnlySet<int> KnownIds => _known;

    public string NamingTitle => _naming == 0 ? "New collection" : "Rename collection";

    /// <summary>Why the typed name cannot be saved, or nothing while it can.</summary>
    public string NameProblem
    {
        get
        {
            string typed = TypedName.Trim();

            if (typed.Length == 0)
            {
                return string.Empty;
            }

            return All.Any(item =>
                       item.Id != _naming
                       && string.Equals(item.Name, typed, StringComparison.CurrentCultureIgnoreCase))
                ? $"There is already a collection called \"{typed}\"."
                : string.Empty;
        }
    }

    public bool HasNameProblem => NameProblem.Length > 0;

    /// <summary>How many albums are ticked, said the way the rule fields say it.</summary>
    public string Chosen
    {
        get
        {
            int chosen = Choices.Count(choice => choice.IsChosen);

            return chosen switch
            {
                0 => "Nothing chosen. Saving with nothing ticked empties the collection.",
                1 => "1 album chosen.",
                _ => $"{chosen:N0} albums chosen.",
            };
        }
    }

    public bool HasChoices => Choices.Count > 0;

    /// <summary>Opens a shelf, so the wall below shows what is on it.</summary>
    /// <remarks>
    /// Nothing is announced through <see cref="Changed"/> for this or for
    /// closing one. Neither writes anything, and the wall hears about it by
    /// watching <see cref="Open"/> - going into a collection and coming out
    /// again should not cost a read of the library each way.
    /// </remarks>
    [RelayCommand]
    private void OpenShelf(CollectionItem? collection) => Open = collection;

    /// <summary>Goes back to the band and the albums on no shelf.</summary>
    [RelayCommand]
    private void Close() => Open = null;

    [RelayCommand]
    private void StartCreating() => StartNaming(0, string.Empty);

    /// <summary>Whether there is an open shelf, and time to do something to it.</summary>
    /// <remarks>
    /// Every command that answers to this reads <see cref="Open"/> again in its
    /// own body rather than trusting that this was true. Remove is a Click
    /// handler with a question in front of it rather than a command binding, so
    /// it runs whether or not this still agrees - and the question is a modal
    /// window, which pumps messages for as long as it is up. A reload finishing
    /// behind it re-points <see cref="Open"/> at whatever it finds, and at
    /// nothing when the shelf has gone.
    /// </remarks>
    private bool CanEditOpen => IsIdle && HasOpen;

    [RelayCommand(CanExecute = nameof(CanEditOpen))]
    private void StartRenaming()
    {
        if (Open is not CollectionItem collection)
        {
            return;
        }

        StartNaming(collection.Id, collection.Name);
    }

    /// <summary>Opens the naming panel, on one shelf or on none for a new one.</summary>
    /// <remarks>
    /// Both commands come through here because the field saying which shelf is
    /// being named is a plain one: the title, the problem line and the Save
    /// button all read it, and none of them hears about it moving. The panel
    /// keeps whatever was last typed in it, so opening it on a shelf whose name
    /// is already in the box writes the string the box already holds, which
    /// announces nothing either - and the panel came up refusing the name of the
    /// very shelf it had been opened to rename, with Save dead until a key was
    /// pressed.
    /// </remarks>
    private void StartNaming(int shelf, string name)
    {
        _naming = shelf;
        TypedName = name;
        OnPropertyChanged(nameof(NamingTitle));
        OnPropertyChanged(nameof(NameProblem));
        OnPropertyChanged(nameof(HasNameProblem));
        SaveNameCommand.NotifyCanExecuteChanged();
        IsNaming = true;
    }

    [RelayCommand]
    private void CancelNaming() => IsNaming = false;

    private bool CanSaveName => IsIdle && TypedName.Trim().Length > 0 && !HasNameProblem;

    [RelayCommand(CanExecute = nameof(CanSaveName))]
    private async Task SaveNameAsync()
    {
        string name = TypedName.Trim();
        int naming = _naming;

        IsBusy = true;
        try
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                ICollectionRepository repository =
                    scope.ServiceProvider.GetRequiredService<ICollectionRepository>();

                if (naming == 0)
                {
                    naming = await repository.CreateAsync(name).ConfigureAwait(true);
                }
                else
                {
                    await repository.RenameAsync(naming, name).ConfigureAwait(true);
                }
            }

            IsNaming = false;
            await ReloadAsync().ConfigureAwait(true);

            // Opening what was just made, because making a shelf is the first
            // half of filling one and nobody makes an empty shelf on purpose.
            Open = All.FirstOrDefault(item => item.Id == naming);
            Raise($"Saved \"{name}\".");
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Raise($"That could not be saved: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the list of albums, ticked where they are on this shelf already.
    /// </summary>
    /// <remarks>
    /// Read fresh rather than handed over by the wall, so what is offered is
    /// what the library holds rather than what the screen last drew - and so
    /// this type does not have to be told about the other's lists.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanEditOpen))]
    private async Task StartPickingAsync()
    {
        if (Open is not CollectionItem collection)
        {
            return;
        }

        int shelf = collection.Id;

        IsBusy = true;
        try
        {
            IReadOnlyList<AlbumSummary> all;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                all = await scope.ServiceProvider
                    .GetRequiredService<IAlbumRepository>()
                    .GetAsync()
                    .ConfigureAwait(true);
            }

            // Only the shelf still open may raise the list, the way the viewer
            // only lets the picture still open fill in its details. Nothing
            // covers the screen while this reads, so the back chevron beside the
            // name stays live, and a list raised for a shelf that has been left
            // behind has a blank heading, ticks describing somewhere else, and a
            // Save that wants an open shelf and so can never light up. By id
            // rather than by row, because a mosaic arriving replaces the row it
            // lands on.
            if (Open?.Id != shelf)
            {
                return;
            }

            // Every album, including the ones standing on another shelf. An
            // album is on one collection, so ticking one of those moves it -
            // and offering only the loose ones would turn moving an album
            // between two collections into a trip to the first one to untick it
            // and a trip back here.
            Dictionary<int, string> named =
                All.ToDictionary(item => item.Id, item => item.Name);

            Choices.Clear();
            foreach (AlbumSummary album in all)
            {
                bool onThisShelf = album.CollectionId == shelf;
                string? elsewhere = !onThisShelf
                                    && album.CollectionId is int on
                                    && named.TryGetValue(on, out string? other)
                    ? other
                    : null;

                Choices.Add(Choice(album, onThisShelf, elsewhere));
            }

            OnPropertyChanged(nameof(HasChoices));
            OnPropertyChanged(nameof(Chosen));
            IsPicking = true;
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Raise($"The albums could not be read: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelPicking() => IsPicking = false;

    /// <summary>One tickable album, counted the way the rule fields count.</summary>
    /// <remarks>
    /// Both of the things that make ticking a line do more than tick it are said
    /// on the line itself rather than only in the hint underneath: a suggestion
    /// is kept by being ticked, and an album on another shelf is taken off it.
    /// </remarks>
    private TickChoice Choice(AlbumSummary album, bool isChosen, string? elsewhere)
    {
        string photos = album.PhotoCount == 1 ? "1 photo" : $"{album.PhotoCount:N0} photos";

        string caption = album.Origin == AlbumOrigin.Proposed
            ? $"{photos} · suggested"
            : photos;

        if (elsewhere is not null)
        {
            caption = $"{caption} · on {elsewhere}";
        }

        var choice = new TickChoice(album.Id, album.Name, caption, isChosen);

        choice.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TickChoice.IsChosen))
            {
                OnPropertyChanged(nameof(Chosen));
            }
        };

        return choice;
    }

    [RelayCommand(CanExecute = nameof(CanEditOpen))]
    private async Task SavePickAsync()
    {
        if (Open is not CollectionItem collection)
        {
            return;
        }

        int shelf = collection.Id;
        string name = collection.Name;
        List<int> ticked = [.. Choices.Where(choice => choice.IsChosen).Select(choice => choice.Id)];

        IsBusy = true;
        try
        {
            CollectionFillResult result;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                result = await scope.ServiceProvider
                    .GetRequiredService<ICollectionRepository>()
                    .SetAlbumsAsync(shelf, ticked)
                    .ConfigureAwait(true);
            }

            IsPicking = false;
            await ReloadAsync().ConfigureAwait(true);
            Open = All.FirstOrDefault(item => item.Id == shelf);
            Raise(Told(result, name));
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Raise($"That could not be saved: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Removes the open collection, leaving its albums on no shelf.</summary>
    [RelayCommand(CanExecute = nameof(CanEditOpen))]
    private async Task DeleteAsync()
    {
        if (Open is not CollectionItem collection)
        {
            return;
        }

        string name = collection.Name;
        int shelf = collection.Id;

        IsBusy = true;
        try
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                await scope.ServiceProvider
                    .GetRequiredService<ICollectionRepository>()
                    .DeleteAsync(shelf)
                    .ConfigureAwait(true);
            }

            Open = null;
            await ReloadAsync().ConfigureAwait(true);
            Raise($"Removed \"{name}\". Every album that was on it is back on the wall.");
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Raise($"That could not be removed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Re-reads the band, keeping whichever shelf is open open.</summary>
    public async Task ReloadAsync()
    {
        IReadOnlyList<CollectionSummary> all;
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            all = await scope.ServiceProvider
                .GetRequiredService<ICollectionRepository>()
                .GetAsync()
                .ConfigureAwait(true);
        }

        int wasOpen = Open?.Id ?? 0;

        All.Clear();
        foreach (CollectionSummary summary in all)
        {
            All.Add(new CollectionItem(summary, CollectionItem.NoCovers));
        }

        _known = [.. all.Select(summary => summary.Id)];

        OnPropertyChanged(nameof(HasAny));
        OnPropertyChanged(nameof(ShelfCount));
        OnPropertyChanged(nameof(KnownIds));
        Open = All.FirstOrDefault(item => item.Id == wasOpen);

        _ = LoadCoversAsync();
    }

    /// <summary>What to say about a shelf that has just been filled.</summary>
    /// <remarks>
    /// The kept count is said out loud rather than folded into the added one.
    /// Keeping a suggestion is a change to the library that outlives this
    /// screen - a kept album is one no later pass may rewrite - and it was not
    /// what the user pressed the button to do. It is also why nothing joining
    /// and nothing leaving is not enough to call a shelf unchanged: a proposal
    /// already standing on it is accepted by this save too.
    ///
    /// <para>Each clause carries the shelf's name itself rather than sharing one
    /// hung on the end of them all. "Added" and "taken off" do not take the same
    /// preposition, so a single ending fits only whichever clause happens to
    /// come last. The second clause of a save that did both drops the noun,
    /// because the first one has already said it.</para>
    /// </remarks>
    private static string Told(CollectionFillResult result, string name)
    {
        if (result.Added == 0 && result.Removed == 0 && result.Kept == 0)
        {
            return $"\"{name}\" is unchanged.";
        }

        string said = (result.Added, result.Removed) switch
        {
            (0, 0) => $"Nothing joined or left \"{name}\".",
            (0, int off) => $"{Counted(off)} taken off \"{name}\".",
            (int on, 0) => $"{Counted(on)} added to \"{name}\".",
            (int on, int off) =>
                $"{Counted(on)} added to \"{name}\", and {off:N0} taken off.",
        };

        // Before where they came from, so "it" is the shelf just named rather
        // than the last collection in that list.
        said = result.Kept switch
        {
            0 => said,
            1 => $"{said} One album on it was a suggestion, and is now yours to keep.",
            int kept => $"{said} {kept:N0} albums on it were suggestions, and are now "
                        + "yours to keep.",
        };

        return result.From.Count == 0
            ? said
            : $"{said} Taken out of {string.Join(" and ", result.From)}.";
    }

    /// <summary>Albums by the number, the way the band's rows count them.</summary>
    private static string Counted(int albums) =>
        albums == 1 ? "1 album" : $"{albums:N0} albums";

    private void Raise(string said) => Changed?.Invoke(this, said);

    /// <summary>
    /// Decodes each shelf's mosaic, a whole shelf at a time.
    /// </summary>
    /// <remarks>
    /// One report per row rather than one per tile: four separate arrivals would
    /// replace the same row four times, and each replacement has to carry the
    /// open shelf across it.
    /// </remarks>
    private async Task LoadCoversAsync()
    {
        List<CollectionItem> waiting =
        [
            .. All.Where(item =>
                item.Summary.CoverThumbnailNames.Count > 0
                && item.Covers.All(cover => cover is null)),
        ];

        if (waiting.Count == 0)
        {
            return;
        }

        var arrived = new Progress<(CollectionItem Item, IReadOnlyList<ImageSource?> Mosaic)>(
            pair => Replace(pair.Item, pair.Mosaic));

        await Task.Run(() => Parallel.ForEachAsync(
            waiting,
            new ParallelOptions { MaxDegreeOfParallelism = DecodeParallelism },
            (item, token) =>
            {
                var mosaic = new ImageSource?[CollectionItem.MosaicTiles];
                for (int tile = 0; tile < item.Summary.CoverThumbnailNames.Count; tile++)
                {
                    mosaic[tile] = TileImageLoader.LoadTile(
                        _store, item.Summary.CoverThumbnailNames[tile]);
                }

                ((IProgress<(CollectionItem, IReadOnlyList<ImageSource?>)>)arrived)
                    .Report((item, mosaic));
                return ValueTask.CompletedTask;
            })).ConfigureAwait(true);
    }

    /// <summary>
    /// Puts the decoded mosaic on the row, carrying the open shelf across it.
    /// </summary>
    /// <remarks>
    /// The rows are records, so this replaces one rather than mutating it - and
    /// without carrying <see cref="Open"/> over, a mosaic arriving would close
    /// whichever shelf the user had just opened.
    /// </remarks>
    private void Replace(CollectionItem item, IReadOnlyList<ImageSource?> mosaic)
    {
        int at = All.IndexOf(item);
        if (at < 0)
        {
            return;
        }

        bool wasOpen = Open == item;
        CollectionItem withCovers = item with { Covers = mosaic };
        All[at] = withCovers;

        if (wasOpen)
        {
            Open = withCovers;
        }
    }
}

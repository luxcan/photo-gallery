using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Imaging;
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
    private void StartCreating()
    {
        _naming = 0;
        TypedName = string.Empty;
        OnPropertyChanged(nameof(NamingTitle));
        IsNaming = true;
    }

    private bool CanEditOpen => IsIdle && HasOpen;

    [RelayCommand(CanExecute = nameof(CanEditOpen))]
    private void StartRenaming()
    {
        _naming = Open!.Id;
        TypedName = Open.Name;
        OnPropertyChanged(nameof(NamingTitle));
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
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or UnauthorizedAccessException)
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
        int shelf = Open!.Id;

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
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or UnauthorizedAccessException)
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
        int shelf = Open!.Id;
        string name = Open.Name;
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
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or UnauthorizedAccessException)
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
        string name = Open!.Name;
        int shelf = Open.Id;

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
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or UnauthorizedAccessException)
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
            All.Add(new CollectionItem(summary, Cover: null));
        }

        _known = [.. all.Select(summary => summary.Id)];

        OnPropertyChanged(nameof(HasAny));
        OnPropertyChanged(nameof(KnownIds));
        Open = All.FirstOrDefault(item => item.Id == wasOpen);

        _ = LoadCoversAsync();
    }

    /// <summary>What to say about a shelf that has just been filled.</summary>
    /// <remarks>
    /// The kept count is said out loud rather than folded into the added one.
    /// Keeping a suggestion is a change to the library that outlives this
    /// screen - a kept album is one no later pass may rewrite - and it was not
    /// what the user pressed the button to do.
    /// </remarks>
    private static string Told(CollectionFillResult result, string name)
    {
        if (result.Added == 0 && result.Removed == 0)
        {
            return $"\"{name}\" is unchanged.";
        }

        List<string> parts = [];

        if (result.Added > 0)
        {
            parts.Add(result.Added == 1 ? "1 album added" : $"{result.Added:N0} albums added");
        }

        if (result.Removed > 0)
        {
            parts.Add(result.Removed == 1
                ? "1 taken off"
                : $"{result.Removed:N0} taken off");
        }

        string said = $"{string.Join(", ", parts)} to \"{name}\".";

        if (result.From.Count > 0)
        {
            said = $"{said} Taken out of {string.Join(" and ", result.From)}.";
        }

        return result.Kept switch
        {
            0 => said,
            1 => $"{said} One of them was a suggestion, and is now yours to keep.",
            int kept => $"{said} {kept:N0} of them were suggestions, and are now yours to keep.",
        };
    }

    private void Raise(string said) => Changed?.Invoke(this, said);

    private async Task LoadCoversAsync()
    {
        List<CollectionItem> waiting =
        [
            .. All.Where(item =>
                item.Cover is null && item.Summary.CoverThumbnailName is not null),
        ];

        if (waiting.Count == 0)
        {
            return;
        }

        var arrived = new Progress<(CollectionItem Item, ImageSource? Picture)>(pair =>
            Replace(pair.Item, pair.Picture));

        await Task.Run(() => Parallel.ForEachAsync(
            waiting,
            new ParallelOptions { MaxDegreeOfParallelism = DecodeParallelism },
            (item, token) =>
            {
                ImageSource? picture = TileImageLoader.LoadTile(
                    _store, item.Summary.CoverThumbnailName);

                ((IProgress<(CollectionItem, ImageSource?)>)arrived).Report((item, picture));
                return ValueTask.CompletedTask;
            })).ConfigureAwait(true);
    }

    /// <summary>
    /// Puts the decoded cover on the card, carrying the open shelf across it.
    /// </summary>
    /// <remarks>
    /// The cards are records, so this replaces one rather than mutating it - and
    /// without carrying <see cref="Open"/> over, a cover arriving would close
    /// whichever shelf the user had just opened.
    /// </remarks>
    private void Replace(CollectionItem item, ImageSource? picture)
    {
        int at = All.IndexOf(item);
        if (at < 0)
        {
            return;
        }

        bool wasOpen = Open == item;
        CollectionItem withCover = item with { Cover = picture };
        All[at] = withCover;

        if (wasOpen)
        {
            Open = withCover;
        }
    }
}

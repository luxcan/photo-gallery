using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;

namespace PhotoGallery.App.Collections;

/// <summary>
/// The occasions in the library: the ones the app suggests, and the ones the
/// user made.
/// </summary>
/// <remarks>
/// Two lists rather than one, because they answer to different rules. A
/// suggestion is a question - keep it or throw it away - and a rebuild may
/// change it. Something the user made is theirs, and no pass touches it.
///
/// <para>Nothing on this screen moves a file. Putting a photograph into a
/// collection moves it out of whichever collection it was in, because a
/// photograph belongs to one occasion, and the screen says so rather than doing
/// it quietly.</para>
/// </remarks>
public sealed partial class CollectionsViewModel : ObservableObject
{
    /// <summary>How many covers are decoded at once, as elsewhere.</summary>
    private const int DecodeParallelism = 4;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThumbnailStore _store;
    private readonly TileWindow _photos;

    /// <summary>
    /// True while the lists are being rebuilt.
    /// </summary>
    /// <remarks>
    /// A two-way bound list writes a null selection while it is being cleared,
    /// and unguarded that reads as the user having chosen something else - so
    /// the photographs of the collection they are looking at would empty
    /// themselves every time the screen refreshed.
    /// </remarks>
    private bool _rebuilding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CreateCollectionCommand), nameof(AcceptCommand),
        nameof(DismissCommand), nameof(RenameCommand), nameof(DeleteCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    /// <summary>Which tab is showing: what the app suggests, or what the user made.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingSuggested), nameof(Showing),
        nameof(HasNone), nameof(EmptyMessage))]
    private bool _showMine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected), nameof(SelectedIsProposed),
        nameof(SelectedIsMine), nameof(SelectedName))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand), nameof(DismissCommand),
        nameof(RenameCommand), nameof(DeleteCommand), nameof(EditCommand))]
    private CollectionItem? _selected;

    /// <summary>The name being typed for a new collection.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCollectionCommand))]
    private string _newName = string.Empty;

    /// <summary>The name being typed over an existing one.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    private string _renameTo = string.Empty;

    /// <summary>
    /// True while the collection is being edited.
    /// </summary>
    /// <remarks>
    /// The renaming box and the keep-or-throw-away buttons live behind one quiet
    /// Edit button rather than along the top of the album. Laid out on the strip
    /// they shouted at somebody who had only come to look at their photographs -
    /// and looking is what this screen is for.
    /// </remarks>
    [ObservableProperty]
    private bool _isEditing;

    public CollectionsViewModel(IServiceScopeFactory scopeFactory, IThumbnailStore store)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _photos = new TileWindow(store);
    }

    /// <summary>Raised when the library's collections have changed.</summary>
    public event EventHandler? LibraryChanged;

    /// <summary>Everything the app is offering, newest occasion first.</summary>
    public ObservableCollection<CollectionItem> Suggested { get; } = [];

    /// <summary>Everything the user kept or made.</summary>
    public ObservableCollection<CollectionItem> Mine { get; } = [];

    /// <summary>The list the visible tab is showing.</summary>
    public ObservableCollection<CollectionItem> Showing => ShowMine ? Mine : Suggested;

    /// <summary>
    /// The other side of <see cref="ShowMine"/>, so each tab binds to a property
    /// that answers for it rather than sharing one inverted.
    /// </summary>
    public bool IsShowingSuggested
    {
        get => !ShowMine;
        set => ShowMine = !value;
    }

    public TileWindow Photos => _photos;

    /// <summary>The rows of pictures the open collection is showing.</summary>
    public System.Collections.ObjectModel.ObservableCollection<GalleryRow> PhotoRows =>
        _photos.Rows;

    /// <summary>How many pictures are in it, for the grid's own bookkeeping.</summary>
    public int PhotoCount => _photos.Tiles.Count;

    public bool HasPhotos => PhotoCount > 0;

    /// <summary>Tells the grid how tall its window is, in rows of pictures.</summary>
    public void SetVisibleRows(int rows) => _photos.SetVisibleRows(rows);

    /// <summary>Re-chunks the rows for a new width.</summary>
    public void SetColumns(int columns) => _photos.SetColumns(columns);

    public int Columns => _photos.Columns;

    public Task ShowRangeAsync(int firstVisibleItem) => _photos.ShowRangeAsync(firstVisibleItem);

    public bool IsIdle => !IsBusy;

    public bool HasStatus => Status.Length > 0;

    public bool HasSelected => Selected is not null;

    public bool SelectedIsProposed => Selected?.IsProposed == true;

    public bool SelectedIsMine => Selected?.IsMine == true;

    public string SelectedName => Selected?.Name ?? string.Empty;

    public bool HasNone => Showing.Count == 0;

    /// <summary>What an empty tab says, which differs by tab.</summary>
    public string EmptyMessage => ShowMine
        ? "Nothing of your own yet. Name one above, then add photographs to it from any picture."
        : "Nothing suggested yet. Scan your folders and the app will group what it finds - "
          + "a weekend away, a day out - and offer them here.";

    /// <summary>Opens one collection on its photographs.</summary>
    /// <remarks>
    /// The screen is two states rather than two panes: a wall of albums, or one
    /// album open. A list beside a grid spends a quarter of the width on names
    /// when the cover is what anybody recognises a holiday by.
    /// </remarks>
    [RelayCommand]
    private void Open(CollectionItem? collection) => Selected = collection;

    /// <summary>Back to the wall of albums.</summary>
    [RelayCommand]
    private void CloseCollection()
    {
        IsEditing = false;
        Selected = null;
    }

    /// <summary>Opens the panel that renames, keeps or throws this one away.</summary>
    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void Edit()
    {
        RenameTo = SelectedName;
        Status = string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    /// <summary>Reads the collections again, keeping whatever was open open.</summary>
    public async Task ReloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            IReadOnlyList<CollectionSummary> all;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                all = await scope.ServiceProvider
                    .GetRequiredService<ICollectionRepository>()
                    .GetAsync()
                    .ConfigureAwait(true);
            }

            Apply(all);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or UnauthorizedAccessException)
        {
            Status = $"The collections could not be read: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Forgets a message that belongs to the last time this was open.</summary>
    public void Reopened() => Status = string.Empty;

    /// <summary>Keeps a suggestion, so no pass may change it again.</summary>
    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private Task AcceptAsync() =>
        AnswerAsync(
            (repository, id) => repository.AcceptAsync(id),
            $"\"{SelectedName}\" is yours now. Scanning will not change it.");

    /// <summary>
    /// Throws a suggestion away, and remembers every photograph that was in it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private Task DismissAsync() =>
        AnswerAsync(
            (repository, id) => repository.DismissAsync(id),
            $"\"{SelectedName}\" will not be suggested again.");

    /// <summary>Removes one of the user's own, leaving its photographs loose.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() =>
        AnswerAsync(
            (repository, id) => repository.DeleteAsync(id),
            $"\"{SelectedName}\" is gone. Its photographs are still in your library.");

    [RelayCommand(CanExecute = nameof(CanRename))]
    private Task RenameAsync()
    {
        string name = RenameTo.Trim();

        return AnswerAsync(
            (repository, id) => repository.RenameAsync(id, name),
            $"Renamed to \"{name}\".");
    }

    /// <summary>Makes an empty collection of the user's own.</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateCollectionAsync()
    {
        string name = NewName.Trim();

        IsBusy = true;
        try
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                await scope.ServiceProvider
                    .GetRequiredService<ICollectionRepository>()
                    .CreateAsync(name)
                    .ConfigureAwait(true);
            }

            NewName = string.Empty;
            Status = $"\"{name}\" is ready. Open a picture and choose Add to a collection.";
            ShowMine = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"That collection could not be made: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await ReloadAsync().ConfigureAwait(true);
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanAnswer() => IsIdle && SelectedIsProposed;

    private bool CanDelete() => IsIdle && SelectedIsMine;

    private bool CanRename() => IsIdle && HasSelected && RenameTo.Trim().Length > 0;

    private bool CanCreate() => IsIdle && NewName.Trim().Length > 0;

    /// <summary>Does one thing to the open collection, then reads the lists again.</summary>
    private async Task AnswerAsync(
        Func<ICollectionRepository, int, Task> answer, string said)
    {
        if (Selected is not CollectionItem collection)
        {
            return;
        }

        IsEditing = false;
        IsBusy = true;
        try
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                await answer(
                    scope.ServiceProvider.GetRequiredService<ICollectionRepository>(),
                    collection.Id).ConfigureAwait(true);
            }

            Status = said;
            RenameTo = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"That could not be done: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await ReloadAsync().ConfigureAwait(true);
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(IReadOnlyList<CollectionSummary> all)
    {
        int wasOpen = Selected?.Id ?? 0;

        _rebuilding = true;
        try
        {
            Suggested.Clear();
            Mine.Clear();

            foreach (CollectionSummary summary in all)
            {
                var item = new CollectionItem(summary, Cover: null);
                if (item.IsProposed)
                {
                    Suggested.Add(item);
                }
                else
                {
                    Mine.Add(item);
                }
            }
        }
        finally
        {
            _rebuilding = false;
        }

        OnPropertyChanged(nameof(HasNone));
        Selected = Showing.FirstOrDefault(item => item.Id == wasOpen);

        _ = LoadCoversAsync();
    }

    partial void OnSelectedChanged(CollectionItem? value)
    {
        if (_rebuilding)
        {
            return;
        }

        RenameTo = value?.Name ?? string.Empty;

        if (value is not null)
        {
            _ = LoadPhotosAsync(value.Id);
        }
    }

    partial void OnShowMineChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNone));
        Selected = Showing.FirstOrDefault();
    }

    /// <summary>Opens one collection on its photographs, in the order they were taken.</summary>
    private async Task LoadPhotosAsync(int collectionId)
    {
        IReadOnlyList<int> members;
        GalleryPage page;

        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            members = await scope.ServiceProvider
                .GetRequiredService<ICollectionRepository>()
                .GetMembersAsync(collectionId)
                .ConfigureAwait(true);

            if (members.Count == 0)
            {
                _photos.Fill(Array.Empty<GalleryTile>());
                OnPropertyChanged(nameof(PhotoCount));
                OnPropertyChanged(nameof(HasPhotos));
                return;
            }

            // RankedAssetIds already means "these, in this order", which is how
            // a typed description is answered - so a collection's grid needs no
            // query of its own.
            page = await scope.ServiceProvider
                .GetRequiredService<QueryGalleryHandler>()
                .HandleAsync(new GalleryQuery(RankedAssetIds: members))
                .ConfigureAwait(true);
        }

        if (Selected?.Id != collectionId)
        {
            return;
        }

        _photos.Fill([.. page.Items.Select(item => new GalleryTile(item))]);
        OnPropertyChanged(nameof(PhotoCount));
        OnPropertyChanged(nameof(HasPhotos));

        await _photos.MarkPreparedAsync(CancellationToken.None).ConfigureAwait(true);
        await _photos.ShowRangeAsync(0).ConfigureAwait(true);
    }

    private async Task LoadCoversAsync()
    {
        List<CollectionItem> waiting =
        [
            .. Suggested.Concat(Mine)
                .Where(item => item.Cover is null && item.Summary.CoverThumbnailName is not null),
        ];

        if (waiting.Count == 0)
        {
            return;
        }

        var arrived = new Progress<(CollectionItem Item, ImageSource? Picture)>(pair =>
        {
            Replace(Suggested, pair.Item, pair.Picture);
            Replace(Mine, pair.Item, pair.Picture);
        });

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
    /// Puts the decoded cover on the row, keeping the row's identity.
    /// </summary>
    /// <remarks>
    /// The rows are records, so this is a replacement rather than a mutation -
    /// and the selection has to be carried across it, or decoding a cover would
    /// close whatever the user had open.
    /// </remarks>
    private void Replace(
        ObservableCollection<CollectionItem> list, CollectionItem item, ImageSource? picture)
    {
        int at = list.IndexOf(item);
        if (at < 0)
        {
            return;
        }

        bool wasOpen = Selected == item;
        CollectionItem withCover = item with { Cover = picture };

        _rebuilding = true;
        try
        {
            list[at] = withCover;
        }
        finally
        {
            _rebuilding = false;
        }

        if (wasOpen)
        {
            Selected = withCover;
        }
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Imaging;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Duplicates;
using PhotoGallery.Application.UseCases.Gallery;

namespace PhotoGallery.App.Duplicates;

/// <summary>
/// The Duplicates screen: the same photograph stored more than once, and what to
/// do about it.
/// </summary>
/// <remarks>
/// The two kinds are never merged. Identical copies are a proof - same bytes,
/// nothing to weigh up. Visually alike copies are a question, because a
/// perceptual hash cannot tell a re-saved copy from the next frame of a burst,
/// and on this library it very often is the next frame.
///
/// <para>Deleting happens a group at a time and only ever to copies the user has
/// left unticked, through the same removal handler the photo viewer uses - so a
/// photograph leaves this library by one route, with one warning, however it is
/// reached.</para>
/// </remarks>
public sealed partial class DuplicatesViewModel : ObservableObject
{
    /// <summary>How many pictures are decoded at once, as the gallery does it.</summary>
    private const int DecodeParallelism = 4;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThumbnailStore _store;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>
    /// Whether the visually-alike list is on screen instead of the identical one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Showing), nameof(HasSets), nameof(IsShowingExact))]
    private bool _showNear;

    [ObservableProperty]
    private bool _hasScanned;

    /// <summary>
    /// The copy being looked at whole, rather than as a 400px tile.
    /// </summary>
    /// <remarks>
    /// A tile is enough to see that two pictures are the same scene and no help
    /// at all in deciding which to keep - which is a question about sharpness,
    /// framing and whose eyes are open. Those need the picture.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspecting), nameof(InspectedPosition),
        nameof(InspectedDetails), nameof(IsInspectedKept), nameof(InspectedDecision))]
    private DuplicateCopyItem? _inspected;

    [ObservableProperty]
    private ImageSource? _inspectedPicture;

    /// <summary>The set the open copy belongs to, so stepping stays inside it.</summary>
    private DuplicateSetItem? _inspectedSet;

    public DuplicatesViewModel(IServiceScopeFactory scopeFactory, IThumbnailStore store)
    {
        _scopeFactory = scopeFactory;
        _store = store;
    }

    /// <summary>
    /// Raised when copies have been deleted, so the counts in the status bar are
    /// stale.
    /// </summary>
    public event EventHandler? LibraryChanged;

    public ObservableCollection<DuplicateSetItem> Exact { get; } = [];

    public ObservableCollection<DuplicateSetItem> Near { get; } = [];

    public bool IsIdle => !IsBusy;

    /// <summary>
    /// The other side of <see cref="ShowNear"/>, so the two tabs can each bind
    /// to a property that answers for it rather than sharing one inverted.
    /// </summary>
    public bool IsShowingExact
    {
        get => !ShowNear;
        set => ShowNear = !value;
    }

    public ObservableCollection<DuplicateSetItem> Showing => ShowNear ? Near : Exact;

    public bool HasSets => Showing.Count > 0;

    public string ExactTab => $"Identical ({Exact.Count:N0})";

    public string NearTab => $"Looks the same ({Near.Count:N0})";

    /// <summary>The groups the user has actually decided about.</summary>
    public IReadOnlyList<DuplicateSetItem> Chosen => [.. Showing.Where(set => set.CanDelete)];

    public bool CanDeleteChosen => Chosen.Count > 0;

    /// <summary>
    /// What the button acting on the whole screen would do.
    /// </summary>
    /// <remarks>
    /// Counted in groups and in copies, because they are different numbers and
    /// the second one is the one that cannot be undone.
    /// </remarks>
    public string ChosenCaption
    {
        get
        {
            IReadOnlyList<DuplicateSetItem> chosen = Chosen;
            if (chosen.Count == 0)
            {
                return "Tick the copy you want to keep in a group and it joins the total here. "
                    + "Groups you have not touched are left alone.";
            }

            int copies = chosen.Sum(set => set.Doomed.Count);
            long bytes = chosen.Sum(set => set.DoomedBytes);

            return $"{chosen.Count:N0} of {Showing.Count:N0} groups decided — "
                + $"{copies:N0} copies would be deleted ({DuplicateScan.Gigabytes(bytes)}).";
        }
    }

    public string DeleteChosenCaption
    {
        get
        {
            IReadOnlyList<DuplicateSetItem> chosen = Chosen;
            return chosen.Count == 0
                ? "Delete in the decided groups"
                : $"Delete in {chosen.Count:N0} "
                  + $"{(chosen.Count == 1 ? "group" : "groups")} "
                  + $"({DuplicateScan.Gigabytes(chosen.Sum(set => set.DoomedBytes))})";
        }
    }

    public bool IsInspecting => Inspected is not null;

    /// <summary>
    /// The facts about the open copy, in the panel every screen shares.
    /// </summary>
    /// <remarks>
    /// Built from what the copy already carries rather than read back from the
    /// index: comparing duplicates is exactly when the size, the resolution and
    /// the fingerprint are wanted, so they arrive with the set.
    /// </remarks>
    public PhotoDetails? InspectedDetails => Inspected?.Details;

    /// <summary>Where in its set the open copy sits.</summary>
    public string InspectedPosition => Inspected is null || _inspectedSet is null
        ? string.Empty
        : $"{_inspectedSet.Copies.IndexOf(Inspected) + 1} of {_inspectedSet.Copies.Count}";

    /// <summary>
    /// Whether the copy on screen is one of the ones that stay.
    /// </summary>
    /// <remarks>
    /// The same tick as the checkbox under the card, reached from the whole
    /// picture - which is where the choice is actually made, and the reason this
    /// screen has a big-picture view at all.
    ///
    /// <para>Deliberately the same fact and not a second one. The button here
    /// used to write the app's suggested keeper, which nothing on this screen
    /// acts on: the ticks decide what Delete takes. Pressing it therefore
    /// appeared to do nothing, and reloaded the screen, which cleared every tick
    /// the user had made.</para>
    /// </remarks>
    public bool IsInspectedKept
    {
        get => Inspected?.IsKept ?? false;
        set
        {
            if (Inspected is DuplicateCopyItem copy)
            {
                copy.IsKept = value;
            }
        }
    }

    /// <summary>
    /// What is going to happen to the copy on screen, said as it stands rather
    /// than as an instruction.
    /// </summary>
    /// <remarks>
    /// Three answers and not two. A group nobody has ticked in is not a group
    /// whose copies are about to go - nothing untouched is part of what any
    /// Delete button does - so saying "Deleting this one" there would be the
    /// screen threatening something it will not do.
    ///
    /// <para>Stated rather than commanded, because in a visually-alike group
    /// several copies may stay and there is no single "keep this one" to
    /// press.</para>
    /// </remarks>
    public string InspectedDecision
    {
        get
        {
            if (Inspected is not DuplicateCopyItem copy || _inspectedSet is null)
            {
                return string.Empty;
            }

            return copy.IsKept
                ? "Keeping this one"
                : _inspectedSet.Kept.Count > 0 ? "Deleting this one" : "Not chosen yet";
        }
    }

    /// <summary>
    /// What "identical" means, in the words of somebody who has to trust it.
    /// </summary>
    /// <remarks>
    /// Written because the sets do not look identical at a glance: seven files
    /// named five minutes apart, all the same bytes, read as a burst until you
    /// know that something wrote one photograph out seven times.
    /// </remarks>
    public string ExactExplanation =>
        "Every file in a set here has exactly the same bytes — same pixels, same "
        + "capture date, same everything. They differ only in name and folder, so "
        + "whichever you keep, no part of the photograph is lost.";

    /// <summary>
    /// Said outright above the visually-alike list, because it is the one place
    /// in this app where approving without looking can lose a photograph.
    /// </summary>
    public string NearWarning =>
        "These look alike but are not the same file. A burst of shots seconds apart "
        + "can score identically, so look at the pictures before ticking anything — "
        + "and you can keep more than one. Keep them all if the group is not a duplicate "
        + "at all, and it will not be offered again.";

    /// <summary>
    /// Looks through the library again.
    /// </summary>
    /// <remarks>
    /// Nothing is read from disk: both kinds come out of hashes taken while the
    /// bytes were already in memory during the prepare pass. Twelve years of
    /// copying between phones, cards and folders is answered in under a second.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task FindAsync()
    {
        IsBusy = true;
        try
        {
            DuplicateScan scan;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                FindDuplicatesHandler handler =
                    scope.ServiceProvider.GetRequiredService<FindDuplicatesHandler>();

                scan = await Task.Run(() => handler.HandleAsync()).ConfigureAwait(true);
            }

            Status = scan.Summary;
            HasScanned = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"The library could not be searched: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Rebuilds the screen from what has been found.</summary>
    public async Task ReloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            DuplicateBoard board;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                GetDuplicatesHandler handler =
                    scope.ServiceProvider.GetRequiredService<GetDuplicatesHandler>();

                board = await Task.Run(() => handler.HandleAsync()).ConfigureAwait(true);
            }

            Apply(board);
            await DecodeAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"The duplicates could not be loaded: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(DuplicateBoard board)
    {
        Exact.Clear();
        foreach (DuplicateSetView set in board.Exact)
        {
            Exact.Add(Watched(new DuplicateSetItem(set)));
        }

        Near.Clear();
        foreach (DuplicateSetView set in board.Near)
        {
            Near.Add(Watched(new DuplicateSetItem(set)));
        }

        OnPropertyChanged(nameof(Showing));
        OnPropertyChanged(nameof(HasSets));
        OnPropertyChanged(nameof(ExactTab));
        OnPropertyChanged(nameof(NearTab));
        NotifyChosen();
    }

    /// <summary>
    /// Keeps the running total honest as choices are made in the groups.
    /// </summary>
    private DuplicateSetItem Watched(DuplicateSetItem set)
    {
        set.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DuplicateSetItem.CanDelete))
            {
                NotifyChosen();
            }
        };

        return set;
    }

    /// <summary>
    /// Settles a group without deleting anything: every copy stays, and it is
    /// never offered again.
    /// </summary>
    /// <remarks>
    /// The answer the screen had no way to record. A burst of shots seconds
    /// apart lands in one visually-alike group and can be several photographs
    /// worth keeping - and having decided that today, being asked again on the
    /// next pass is the app failing to listen.
    ///
    /// <para>No confirmation: nothing is destroyed, no file is touched. What it
    /// costs is being asked again, and that is the point of pressing it.</para>
    ///
    /// <para>The command belongs to the shell rather than to this screen, for
    /// the reason deleting's does: settling the group is one statement, and
    /// putting this screen back together afterwards is seconds, which needs the
    /// one overlay - and the shell owns it.</para>
    /// </remarks>
    public async Task KeepEverythingAsync(DuplicateSetItem? set)
    {
        if (set is null)
        {
            return;
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IDuplicateRepository>()
                .MarkResolvedAsync(set.Id, true)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"That decision could not be saved: {ex.Message}";
            return;
        }

        Status = $"Keeping all {set.Copies.Count:N0} copies. This group will not be offered again.";
        await ReloadAsync().ConfigureAwait(true);
    }

    private void NotifyChosen()
    {
        OnPropertyChanged(nameof(Chosen));
        OnPropertyChanged(nameof(CanDeleteChosen));
        OnPropertyChanged(nameof(ChosenCaption));
        OnPropertyChanged(nameof(DeleteChosenCaption));
    }


    /// <summary>Reads each copy's tile off the local disk.</summary>
    /// <remarks>
    /// Tiles rather than previews: this screen shows two pictures side by side to
    /// be compared, not examined, and the 400px copies make a screenful of sets
    /// appear at once.
    /// </remarks>
    private async Task DecodeAsync()
    {
        List<DuplicateCopyItem> waiting =
        [
            .. Exact.Concat(Near)
                .SelectMany(set => set.Copies)
                .Where(copy => copy.Picture is null && copy.Copy.ThumbnailName is not null),
        ];

        if (waiting.Count == 0)
        {
            return;
        }

        // Built on the UI thread so its callback comes back here, as every other
        // pass in this app does.
        var arrived = new Progress<(DuplicateCopyItem Item, ImageSource? Picture)>(
            pair => pair.Item.Picture = pair.Picture);

        await Task.Run(() => Parallel.ForEachAsync(
            waiting,
            new ParallelOptions { MaxDegreeOfParallelism = DecodeParallelism },
            (item, token) =>
            {
                ImageSource? picture = TileImageLoader.LoadTile(
                    _store, item.Copy.ThumbnailName!);

                ((IProgress<(DuplicateCopyItem, ImageSource?)>)arrived).Report((item, picture));
                return ValueTask.CompletedTask;
            })).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens one copy on the whole picture, with everything known about the
    /// file beside it.
    /// </summary>
    [RelayCommand]
    private Task InspectCopyAsync(DuplicateCopyItem? copy)
    {
        if (copy is null)
        {
            return Task.CompletedTask;
        }

        _inspectedSet = Showing.FirstOrDefault(set => set.Copies.Contains(copy));
        return ShowAsync(copy);
    }

    [RelayCommand]
    private void CloseInspect()
    {
        Inspected = null;
        InspectedPicture = null;
        _inspectedSet = null;
    }

    [RelayCommand]
    private Task InspectNextAsync() => StepAsync(1);

    [RelayCommand]
    private Task InspectPreviousAsync() => StepAsync(-1);

    /// <summary>
    /// Moves through the copies in one set, without wrapping.
    /// </summary>
    /// <remarks>
    /// Stopping at either end is what makes the last copy look like the last
    /// copy. Comparing three pictures means knowing when you have seen them all.
    /// </remarks>
    private Task StepAsync(int delta)
    {
        if (Inspected is null || _inspectedSet is null)
        {
            return Task.CompletedTask;
        }

        int next = _inspectedSet.Copies.IndexOf(Inspected) + delta;
        return next < 0 || next >= _inspectedSet.Copies.Count
            ? Task.CompletedTask
            : ShowAsync(_inspectedSet.Copies[next]);
    }

    private async Task ShowAsync(DuplicateCopyItem copy)
    {
        Inspected = copy;
        InspectedPicture = null;

        if (copy.Copy.ThumbnailName is not string rendition)
        {
            return;
        }

        ImageSource? picture = await Task
            .Run(() => TileImageLoader.LoadPreview(_store, rendition))
            .ConfigureAwait(true);

        // Holding an arrow down starts a decode per press. Only the copy still
        // being looked at may paint, or whichever lands last wins.
        if (ReferenceEquals(Inspected, copy))
        {
            InspectedPicture = picture;
        }
    }

    /// <summary>
    /// Follows the open copy's tick, so the toggle over the picture and the
    /// checkbox under the card are one control in two places.
    /// </summary>
    /// <remarks>
    /// Subscribed to the copy rather than to its group, so it holds however the
    /// group reached the screen, and stepping to the next copy carries the
    /// subscription along with it.
    /// </remarks>
    partial void OnInspectedChanged(DuplicateCopyItem? oldValue, DuplicateCopyItem? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnInspectedCopyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnInspectedCopyChanged;
        }
    }

    private void OnInspectedCopyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateCopyItem.IsKept))
        {
            OnPropertyChanged(nameof(IsInspectedKept));
            OnPropertyChanged(nameof(InspectedDecision));
        }
    }

    /// <summary>
    /// What deleting the unticked copies of one group would cost.
    /// </summary>
    /// <remarks>
    /// Read before the question is asked, so the confirmation can name the files
    /// and say whether the Recycle Bin applies. The view owns the asking,
    /// because a modal dialog is a view's job.
    /// </remarks>
    public Task<IReadOnlyList<PhotoToRemove>> DescribeDeletionAsync(DuplicateSetItem? set) =>
        DescribeDeletionAsync(set is null ? [] : [set]);

    /// <summary>
    /// What deleting the unchosen copies of several groups would cost.
    /// </summary>
    public async Task<IReadOnlyList<PhotoToRemove>> DescribeDeletionAsync(
        IReadOnlyList<DuplicateSetItem> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        var photos = new List<PhotoToRemove>();
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            RemovePhotoHandler handler =
                scope.ServiceProvider.GetRequiredService<RemovePhotoHandler>();

            int[] doomed =
            [
                .. sets.Where(set => set.CanDelete)
                    .SelectMany(set => set.Doomed)
                    .Select(copy => copy.AssetId),
            ];

            // Off the dispatcher: this is four queries per copy, and deciding
            // four hundred groups at once meant a window frozen for seconds
            // before the question even appeared.
            await Task.Run(async () =>
            {
                foreach (int assetId in doomed)
                {
                    if (await handler.DescribeAsync(assetId).ConfigureAwait(false)
                        is PhotoToRemove photo)
                    {
                        photos.Add(photo);
                    }
                }
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"Those copies could not be read: {ex.Message}";
            return [];
        }

        return photos;
    }

    /// <summary>
    /// Settles the groups whose copies have just been deleted.
    /// </summary>
    /// <remarks>
    /// The deleting itself belongs to the shell, which owns the one overlay that
    /// reports it - the same path the photo viewer's delete uses, so a
    /// photograph leaves this library one way rather than two. This is only what
    /// the duplicates screen has left to do about it.
    ///
    /// <para>A group is settled only when every copy it gave up actually went. A
    /// copy that would not move leaves its group unfinished and on screen,
    /// because taking the question away without answering it would hide a file
    /// still sitting on the disk.</para>
    ///
    /// <para>Renditions are shared by content, so deleting six of seven
    /// identical files leaves the picture the survivor draws itself with
    /// untouched - that is the removal handler's own rule, not something this
    /// screen has to arrange.</para>
    /// </remarks>
    public async Task AfterDeletedAsync(
        IReadOnlyList<DuplicateSetItem> sets, PhotoRemovalResult result)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(result);

        // Both, because a group is finished only when every copy in it was
        // actually dealt with. A copy on a share that dropped part way through
        // is every bit as unfinished as one that refused to move - more so, since
        // nobody has even been able to look at it.
        HashSet<int> unfinished = [.. result.Refused, .. result.OutOfReach];

        IsBusy = true;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IDuplicateRepository duplicates =
                scope.ServiceProvider.GetRequiredService<IDuplicateRepository>();

            foreach (DuplicateSetItem set in sets.Where(set => set.CanDelete))
            {
                // Only once every copy in it went. A group still holding a file
                // that would not go is a group with something left to do, and
                // settling it would take the question away without answering it.
                if (!set.Doomed.Any(copy => unfinished.Contains(copy.AssetId)))
                {
                    await duplicates.MarkResolvedAsync(set.Id, true).ConfigureAwait(true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"Those groups could not be settled: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        Status = result.OutOfReach.Count > 0
            ? $"{result.Deleted:N0} copies deleted. {result.OutOfReach.Count:N0} were left "
              + $"alone because {string.Join(", ", result.UnreachableSources)} could not be "
              + "reached - nothing about them has been changed."
            : result.Refused.Count == 0
                ? $"{result.Deleted:N0} copies deleted."
                : $"{result.Deleted:N0} copies deleted. "
                  + $"{result.Refused.Count:N0} would not go and are still there.";

        if (result.Deleted > 0)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Moves everything that reads the visible list on to the other tab.
    /// </summary>
    /// <remarks>
    /// The totals and the button acting on the whole screen are computed from
    /// whichever list is showing, so they have to be re-asked here. Without it
    /// they kept the other tab's answer: ticking a copy under "Looks the same"
    /// left the button disabled because it was still reporting on the identical
    /// list, and a button carried the other way round was enabled and did
    /// nothing when pressed.
    /// </remarks>
    partial void OnShowNearChanged(bool value)
    {
        OnPropertyChanged(nameof(Showing));
        NotifyChosen();
    }
}

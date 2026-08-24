using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.Imaging;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Domain.People;

namespace PhotoGallery.App.People;

/// <summary>
/// The People screen: who has been named, and what the app thinks is also them.
/// </summary>
/// <remarks>
/// Naming happens in the photo viewer, where the user is looking at a picture
/// they recognise. This screen keeps the register - add somebody, see how much
/// of the library they account for, answer what has been proposed, remove a name
/// that was a mistake.
/// </remarks>
public sealed partial class PeopleViewModel : ObservableObject
{
    /// <summary>
    /// How many crops are decoded at once. The same figure the gallery uses:
    /// each one reads a preview and cuts a face out of it, so the work is a
    /// decode rather than a wait.
    /// </summary>
    private const int DecodeParallelism = 4;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThumbnailStore _store;

    /// <summary>
    /// The selected person's pictures, and the bound on how many of them are
    /// decoded at once. The same window the library grid uses.
    /// </summary>
    private readonly TileWindow _photos;

    /// <summary>
    /// True while the screen is being rebuilt.
    /// </summary>
    /// <remarks>
    /// Every command that acts on the screen has to be told when this changes. A
    /// relay command re-asks whether it can run only when notified, and controls
    /// built while a reload is in progress otherwise evaluate once, find the
    /// screen busy, and stay disabled for good.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmProposalsCommand), nameof(RejectProposalsCommand),
        nameof(AddPersonCommand), nameof(ForgetPersonCommand),
        nameof(OpenConfirmQueueCommand), nameof(BackToPhotosCommand),
        nameof(SetBirthYearCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPerson), nameof(ShowingPhotos),
        nameof(ShowingConfirmQueue), nameof(HasPendingConfirm), nameof(ConfirmCaption))]
    private PersonItem? _selectedPerson;

    [ObservableProperty]
    private string _selectedPersonName = string.Empty;

    /// <summary>
    /// True while the proposal queue is being answered rather than the person's
    /// own pictures being looked at.
    /// </summary>
    /// <remarks>
    /// Opening somebody shows what is settled about them - their pictures - and
    /// the questions are a place you go, once, by choosing to. It used to be the
    /// other way round: a name opened onto a wall of crops to be judged, which
    /// made the register of who is in the library into a queue of work.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowingPhotos), nameof(ShowingConfirmQueue))]
    private bool _isConfirming;

    /// <summary>What has been typed into the year of birth box.</summary>
    [ObservableProperty]
    private string _birthYearText = string.Empty;

    [ObservableProperty]
    private string _birthYearError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhotos), nameof(PhotoCaption))]
    private int _photoCount;

    /// <summary>
    /// How many of the pictures on screen were placed by their file date rather
    /// than a capture date, so an age read off them may be too old.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AgeNote))]
    private int _datedFromTheFile;

    /// <summary>A name being added by hand, before any face is attached to it.</summary>
    [ObservableProperty]
    private string _newPersonName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFaces), nameof(HasNoFaces), nameof(FacesCaption))]
    private int _totalFaces;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FacesCaption))]
    private int _namedFaces;

    /// <summary>
    /// The proposal being looked at whole, rather than as a crop.
    /// </summary>
    /// <remarks>
    /// A crop is enough to answer most questions and no help at all with the
    /// ones that are actually hard: a face at eighty pixels, half of it hair,
    /// tells you very little about whether it is one child at four or a cousin
    /// at six. The picture it came out of nearly always does.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspecting), nameof(InspectedPosition))]
    private FaceCropItem? _inspected;

    [ObservableProperty]
    private ImageSource? _inspectedPicture;

    /// <summary>
    /// The facts about the photograph the open face came out of, in the panel
    /// every screen shares.
    /// </summary>
    [ObservableProperty]
    private PhotoDetails? _inspectedDetails;

    /// <summary>
    /// What has been answered since the picture was opened.
    /// </summary>
    /// <remarks>
    /// Shown while the queue is being worked, so the shrinking count of faces
    /// left has a growing count of faces done beside it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnsweredCaption))]
    private int _kept;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnsweredCaption))]
    private int _leftOut;

    /// <summary>How many were somebody else after all.</summary>
    /// <remarks>
    /// Counted apart from the ones left out because it is a different answer.
    /// "Not her" leaves a face unnamed; "that is her brother" names it, and the
    /// second one is worth far more to the app than the first.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnsweredCaption))]
    private int _moved;

    /// <summary>
    /// How many photographs were deleted while the picture was open.
    /// </summary>
    /// <remarks>
    /// Not shown anywhere - deleting is not an answer about anybody, so it has
    /// no place in the tally. It is counted because it still changes what the
    /// rest of the screen says, and closing has to know whether there is
    /// anything to bring up to date.
    /// </remarks>
    private int _photosRemoved;

    /// <summary>
    /// What the badge beside the turn buttons says, or null for no badge.
    /// </summary>
    /// <remarks>
    /// The same badge the photo viewer shows, for the same reason: "the file
    /// itself now knows", "only this app knows" and "nobody could ask the file"
    /// are three different situations, and the difference is invisible until the
    /// picture is opened somewhere else. See <see cref="TurnNotices"/>.
    /// </remarks>
    [ObservableProperty]
    private string? _turnNotice;

    /// <summary>The longer form of <see cref="TurnNotice"/>, shown on hover.</summary>
    [ObservableProperty]
    private string? _turnNoticeTip;

    private double _inspectAreaWidth;
    private double _inspectAreaHeight;

    /// <summary>
    /// True while a reload is putting back the person already chosen, so that
    /// restoring a choice is not mistaken for making one.
    /// </summary>
    private bool _restoring;

    public PeopleViewModel(IServiceScopeFactory scopeFactory, IThumbnailStore store)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _photos = new TileWindow(store);

        // No "nobody" here. On this screen the face is already somebody's
        // proposal, and "leave it out" is the answer for one that is not a
        // person at all - offering two ways to say the same thing would only
        // make the user choose between them.
        Reassign = new PersonPicker(ReassignInspectedToAsync);

        Named.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNamed));
            OnPropertyChanged(nameof(HasNoNamed));
        };

        Proposals.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasProposals));
            OnPropertyChanged(nameof(QuestionCaption));
            OnPropertyChanged(nameof(InspectedPosition));
        };
    }

    /// <summary>
    /// Raised when this screen has changed what the library holds - a photograph
    /// deleted, a person added or forgotten - so the counts in the status bar
    /// are stale.
    /// </summary>
    public event EventHandler? LibraryChanged;

    /// <summary>
    /// Raised when a turn was refused because the photograph's folder is away.
    /// </summary>
    /// <remarks>
    /// The same event the photo viewer raises, answered by the same dialog, so
    /// being refused reads identically wherever the turn was attempted from.
    /// </remarks>
    public event EventHandler<IReadOnlyList<string>>? TurnRefusedOutOfReach;

    public ObservableCollection<PersonItem> Named { get; } = [];

    public ObservableCollection<FaceCropItem> Proposals { get; } = [];

    public ObservableCollection<PersonEra> Eras { get; } = [];

    /// <summary>
    /// The selected person's confirmed pictures, cut into the ages they were.
    /// </summary>
    /// <remarks>
    /// The same window the library uses, so a person with thousands of pictures costs
    /// the same bounded amount of memory the library does - the grid decodes the
    /// visible screen and half a screen either side, and releases the rest.
    /// </remarks>
    public ObservableCollection<GalleryRow> PhotoRows => _photos.Rows;

    /// <summary>
    /// The grid itself, so the shared viewer can be told to step through these
    /// pictures rather than the library's.
    /// </summary>
    public TileWindow Photos => _photos;

    /// <summary>The pictures that are settled, not the questions.</summary>
    public bool ShowingPhotos => HasSelectedPerson && !IsConfirming;

    /// <summary>The queue of faces the app thinks are them.</summary>
    public bool ShowingConfirmQueue => HasSelectedPerson && IsConfirming;

    public bool HasPendingConfirm => SelectedPerson?.HasReview == true;

    /// <summary>
    /// The button that opens the queue, counting the questions it will ask.
    /// </summary>
    public string ConfirmCaption =>
        $"Confirm photos ({SelectedPerson?.AwaitingReview ?? 0:N0})";

    public bool HasPhotos => PhotoCount > 0;

    public string PhotoCaption => PhotoCount == 1
        ? "1 picture"
        : $"{PhotoCount:N0} pictures";

    /// <summary>
    /// Said once, under the year of birth, rather than on every heading.
    /// </summary>
    public string AgeNote
    {
        get
        {
            if (SelectedPerson?.Summary.BirthYear is null)
            {
                return "Add the year they were born to group these by age.";
            }

            string convention =
                "Ages are the age they reach that year - without a full date of "
                + "birth the app cannot tell which side of a birthday a picture falls.";

            return DatedFromTheFile == 0
                ? convention
                : $"{convention} {DatedFromTheFile:N0} of these carry no capture "
                  + "date, so their age comes from the file's date and may be too old.";
        }
    }

    /// <summary>
    /// The name list for a face the app has guessed wrong.
    /// </summary>
    /// <remarks>
    /// The same one the photo viewer uses. Answering "no" to a face that is
    /// plainly her brother throws the answer away - the app learns only that it
    /// was wrong, not what was right - and correcting it here is the difference
    /// between a rejection and an example.
    /// </remarks>
    public PersonPicker Reassign { get; }

    public bool IsIdle => !IsBusy;

    public bool HasFaces => TotalFaces > 0;

    public bool HasNoFaces => TotalFaces == 0;

    public bool HasSelectedPerson => SelectedPerson is not null;

    public bool HasNamed => Named.Count > 0;

    public bool HasNoNamed => Named.Count == 0;

    public bool HasProposals => Proposals.Count > 0;

    /// <summary>Whether there is anything to report.</summary>
    /// <remarks>
    /// The picture covers the header the status line lives in, so the inspector
    /// carries its own - and a line that is there but blank reserves a gap under
    /// the heading for nothing.
    /// </remarks>
    public bool HasStatus => Status.Length > 0;

    /// <summary>How many questions are left, counted as a sentence.</summary>
    /// <remarks>
    /// Spelt out rather than formatted from the count, because the last question
    /// is the one most likely to be read - and it read "1 faces look like them".
    /// Every other count on this screen already spells its singular.
    /// </remarks>
    public string QuestionCaption => Proposals.Count == 1
        ? "1 face looks like them and is waiting to be checked"
        : $"{Proposals.Count:N0} faces look like them and are waiting to be checked";

    public bool IsInspecting => Inspected is not null;

    /// <summary>Where in the queue the open proposal sits.</summary>
    /// <remarks>
    /// The total shrinks as answers are given, because an answered face leaves
    /// the queue. That is the point: what is on screen is what is still to do.
    /// </remarks>
    public string InspectedPosition => Inspected is null
        ? string.Empty
        : $"{Proposals.IndexOf(Inspected) + 1:N0} of {Proposals.Count:N0} left";

    public string AnsweredCaption =>
        Kept + LeftOut + Moved == 0 ? string.Empty : $"{Answered(Kept, LeftOut, Moved)} so far";

    public string FacesCaption => TotalFaces == 0
        ? "No faces have been found yet."
        : $"{NamedFaces:N0} of {TotalFaces:N0} faces have a name.";

    /// <summary>
    /// Rebuilds the screen from the faces on record.
    /// </summary>
    /// <remarks>
    /// Run every time the section is opened, not once. Naming a face happens in
    /// the photo viewer, so by the time this screen is looked at again there may
    /// be a person on it that did not exist when it last loaded - which is
    /// exactly what a load-once screen cannot show.
    ///
    /// <para>Failures are reported rather than thrown. This is started without
    /// being awaited, so an exception would go unobserved and leave the screen
    /// permanently busy.</para>
    /// </remarks>
    public async Task ReloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            PeopleBoard board;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                GetPeopleBoardHandler handler =
                    scope.ServiceProvider.GetRequiredService<GetPeopleBoardHandler>();

                // Off the dispatcher: it reads every face in the library.
                board = await Task.Run(() => handler.HandleAsync()).ConfigureAwait(true);
            }

            Apply(board);

            if (SelectedPerson is not null)
            {
                await LoadPersonAsync(SelectedPerson.Id).ConfigureAwait(true);
            }

            // Always, not only on the way back to the list. Rebuilding the board
            // replaces every row with a new one whose picture is empty, and the
            // list stays on screen while somebody is open - so loading the faces
            // only when nobody was selected left a column of holes behind them.
            // It returns immediately when they are all already there.
            await LoadCoversAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or UnauthorizedAccessException)
        {
            Status = $"The people could not be loaded: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(PeopleBoard board)
    {
        TotalFaces = board.TotalFaces;
        NamedFaces = board.NamedFaces;

        int? keep = SelectedPerson?.Id;

        // The guard covers emptying the list, not only putting the selection
        // back. Named is the list box's items and its selected item is bound
        // both ways, so clearing it writes a null selection into this view model
        // an instant before the new rows exist - and unguarded that read as the
        // user having chosen somebody else, which closes the questions and
        // throws away the queue. Closing a picture rebuilds this board, so
        // answering one face and pressing Close landed back on the person's
        // photographs with the other two hundred questions gone.
        _restoring = true;
        try
        {
            Named.Clear();
            foreach (PersonSummary person in board.People)
            {
                Named.Add(new PersonItem(person));
            }

            SelectedPerson = Named.FirstOrDefault(person => person.Id == keep);
        }
        finally
        {
            _restoring = false;
        }

        CloseInspection();

        // The questions belong to whoever is selected, so they are thrown away
        // only when the board came back without them. Clearing unconditionally
        // is what emptied the queue behind the inspector: answering a face
        // already takes it out of the list, and closing the picture rebuilds
        // this board to bring the counts up to date - which then discarded
        // every question still to answer, and Close landed on a screen with
        // nothing on it instead of on the rest of the queue.
        if (SelectedPerson?.Id != keep)
        {
            Proposals.Clear();
            Eras.Clear();
        }

        SelectedPersonName = SelectedPerson?.DisplayName ?? string.Empty;
    }

    partial void OnSelectedPersonChanged(PersonItem? value)
    {
        if (_restoring)
        {
            return;
        }

        CloseInspection();
        Proposals.Clear();
        Eras.Clear();
        SelectedPersonName = value?.DisplayName ?? string.Empty;

        // What the line under the count was saying was about whoever was open
        // before - "16 faces are Elsa" sitting under Vera's name is a
        // sentence about the wrong person.
        Status = string.Empty;

        // Choosing a name always lands on their pictures. Staying in the queue
        // would carry one person's questions over onto the next person's name.
        IsConfirming = false;

        if (value is not null)
        {
            _ = LoadPersonAsync(value.Id);
        }
    }

    /// <summary>
    /// Opens somebody on what is settled about them: every picture their face
    /// has been confirmed in, newest first, under the age they were.
    /// </summary>
    /// <remarks>
    /// The proposal queue is deliberately not read here. It costs a walk of
    /// every face row in the library, and it answers a question the user has not
    /// asked yet - choosing a name is "show me them", not "give me work".
    /// </remarks>
    private async Task LoadPersonAsync(int personId)
    {
        GalleryPage page;
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            page = await scope.ServiceProvider
                .GetRequiredService<QueryGalleryHandler>()

                // PersonId already means confirmed faces only, in every folder,
                // videos included - the same filter the library's own person
                // view uses, rather than a second one that could disagree.
                .HandleAsync(new GalleryQuery(PersonId: personId))
                .ConfigureAwait(true);
        }

        if (SelectedPerson?.Id != personId)
        {
            return;
        }

        _pictures = [.. page.Items];
        PhotoCount = page.TotalCount;
        BirthYearText = SelectedPerson.Summary.BirthYear?.ToString(CultureInfo.CurrentCulture)
            ?? string.Empty;
        BirthYearError = string.Empty;

        Regroup(fresh: true);
        await _photos.MarkPreparedAsync(CancellationToken.None).ConfigureAwait(true);
        await _photos.ShowRangeAsync(0).ConfigureAwait(true);
    }

    private IReadOnlyList<GalleryItem> _pictures = [];

    /// <summary>
    /// Cuts the pictures into ages again, without refetching or throwing away
    /// bitmaps already decoded - typing a year regroups what is already there.
    /// </summary>
    private void Regroup(bool fresh)
    {
        IReadOnlyList<AgeGroup> groups =
            PersonPhotoGrouping.Into(_pictures, SelectedPerson?.Summary.BirthYear);

        DatedFromTheFile = groups.Sum(group => group.DatedFromTheFile);

        List<TileGroup> tiles = [.. groups.Select(group => new TileGroup(
            group.Heading,
            group.Photos.Count == 1 ? "1 picture" : $"{group.Photos.Count:N0} pictures",
            group.IsEntirelyInferred ? "dated from the files, not the camera" : null,
            [.. group.Photos.Select(photo => new GalleryTile(photo))]))];

        if (fresh)
        {
            _photos.Fill(tiles);
        }
        else
        {
            // Same pictures, different cuts. Rebuilding the tiles would blank
            // the screen and decode it all again for a change of heading.
            _photos.Regroup([.. Regrouped(tiles)]);
        }

        OnPropertyChanged(nameof(AgeNote));
    }

    /// <summary>
    /// Re-cut groups carrying the tiles that already exist, so nothing decoded
    /// is thrown away when only the grouping changed.
    /// </summary>
    private IEnumerable<TileGroup> Regrouped(IReadOnlyList<TileGroup> wanted)
    {
        Dictionary<int, GalleryTile> existing =
            _photos.Tiles.ToDictionary(tile => tile.Item.Id);

        foreach (TileGroup group in wanted)
        {
            yield return group with
            {
                Tiles =
                [
                    .. group.Tiles.Select(tile =>
                        existing.TryGetValue(tile.Item.Id, out GalleryTile? kept) ? kept : tile),
                ],
            };
        }
    }

    /// <summary>Records the year, or says why it was not taken.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SetBirthYearAsync()
    {
        if (SelectedPerson is not PersonItem person)
        {
            return;
        }

        string typed = BirthYearText.Trim();
        int? year = null;

        if (typed.Length > 0)
        {
            if (!int.TryParse(typed, NumberStyles.None, CultureInfo.CurrentCulture, out int parsed)
                || !PersonAge.IsPlausible(parsed, DateTime.Today))
            {
                BirthYearError =
                    $"A year between {PersonAge.EarliestYear} and {DateTime.Today.Year}.";
                return;
            }

            year = parsed;
        }

        if (year == person.Summary.BirthYear)
        {
            BirthYearError = string.Empty;
            return;
        }

        BirthYearError = string.Empty;
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IPeopleRepository>()
                .SetBirthYearAsync(person.Id, year)
                .ConfigureAwait(true);
        }

        person.RememberBirthYear(year);
        OnPropertyChanged(nameof(HasPendingConfirm));
        Regroup(fresh: false);
    }

    /// <summary>Goes to the questions, which is a place rather than the default.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenConfirmQueueAsync()
    {
        if (SelectedPerson is not PersonItem person)
        {
            return;
        }

        IsConfirming = true;

        PersonReview? review;
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            review = await scope.ServiceProvider
                .GetRequiredService<GetPersonReviewHandler>()
                .HandleAsync(person.Id)
                .ConfigureAwait(true);
        }

        if (review is null || SelectedPerson?.Id != person.Id)
        {
            return;
        }

        CloseInspection();
        Proposals.Clear();
        foreach (FaceThumbnail face in review.Proposed)
        {
            Proposals.Add(new FaceCropItem(face));
        }

        Eras.Clear();
        foreach (PersonEra era in review.Eras)
        {
            Eras.Add(era);
        }

        await DecodeAsync([.. Proposals]).ConfigureAwait(true);
    }

    /// <summary>
    /// Back from the questions to the pictures, reloading them because answering
    /// is exactly what changes which pictures are theirs.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BackToPhotosAsync()
    {
        IsConfirming = false;
        CloseInspection();
        Proposals.Clear();

        if (SelectedPerson is PersonItem person)
        {
            await LoadPersonAsync(person.Id).ConfigureAwait(true);
        }
    }

    /// <summary>Tells the grid how tall its window is, in rows of pictures.</summary>
    public void SetVisibleRows(int rows) => _photos.SetVisibleRows(rows);

    /// <summary>Re-chunks the rows for a new width.</summary>
    public void SetColumns(int columns) => _photos.SetColumns(columns);

    public int Columns => _photos.Columns;

    public Task ShowRangeAsync(int firstVisibleItem) => _photos.ShowRangeAsync(firstVisibleItem);

    /// <summary>Reads each crop off disk and hands it back on the dispatcher.</summary>
    private async Task DecodeAsync(IReadOnlyList<FaceCropItem> items)
    {
        if (items.Count > 0)
        {
            // Created here, on the UI thread, so its callback comes back here
            // too - the same reason every other pass builds one where it starts.
            var arrived = new Progress<(FaceCropItem Item, ImageSource? Picture)>(
                pair => pair.Item.Picture = pair.Picture);

            // A photograph at a time rather than a face at a time. Faces come in
            // groups - a group shot can be eight of these - and each crop used to
            // read and decode the whole preview again to cut one rectangle out
            // of it.
            IEnumerable<IGrouping<string, FaceCropItem>> byPicture =
                items.GroupBy(item => item.Face.ThumbnailName, StringComparer.OrdinalIgnoreCase);

            await Task.Run(() => Parallel.ForEachAsync(
                byPicture,
                new ParallelOptions { MaxDegreeOfParallelism = DecodeParallelism },
                (samePicture, token) =>
                {
                    BitmapSource? preview =
                        TileImageLoader.LoadPreview(_store, samePicture.Key) as BitmapSource;

                    foreach (FaceCropItem item in samePicture)
                    {
                        ImageSource? picture = preview is null
                            ? null
                            : TileImageLoader.CutFaceFrom(preview, item.Face.Bounds);

                        ((IProgress<(FaceCropItem, ImageSource?)>)arrived).Report((item, picture));
                    }

                    return ValueTask.CompletedTask;
                })).ConfigureAwait(true);
        }

        await LoadCoversAsync().ConfigureAwait(true);
    }

    private async Task LoadCoversAsync()
    {
        List<PersonItem> waiting =
            [.. Named.Where(person => person.Picture is null && person.Summary.Cover is not null)];

        if (waiting.Count == 0)
        {
            return;
        }

        var arrived = new Progress<(PersonItem Item, ImageSource? Picture)>(
            pair => pair.Item.Picture = pair.Picture);

        await Task.Run(() => Parallel.ForEachAsync(
            waiting,
            new ParallelOptions { MaxDegreeOfParallelism = DecodeParallelism },
            (person, token) =>
            {
                FaceThumbnail cover = person.Summary.Cover!;
                ImageSource? picture = TileImageLoader.LoadFaceCrop(
                    _store, cover.ThumbnailName, cover.Bounds);

                ((IProgress<(PersonItem, ImageSource?)>)arrived).Report((person, picture));
                return ValueTask.CompletedTask;
            })).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens one proposal on the whole photograph it was cut from, with the face
    /// in question outlined.
    /// </summary>
    /// <remarks>
    /// Given no face, the queue is opened at the top - which is what makes this
    /// usable as "check these one at a time" as well as "show me that one".
    /// </remarks>
    [RelayCommand]
    private Task InspectFaceAsync(FaceCropItem? face)
    {
        FaceCropItem? target = face ?? Proposals.FirstOrDefault();
        if (target is null)
        {
            return Task.CompletedTask;
        }

        // Whatever the line under the heading was last saying belongs to the
        // screen this one is about to cover.
        Status = string.Empty;
        return ShowAsync(target);
    }

    /// <summary>
    /// Closes the picture and works out what the answers just given mean.
    /// </summary>
    [RelayCommand]
    private async Task CloseInspectAsync()
    {
        // The name list sits over the picture, so Close means put that down
        // first - closing the whole screen from underneath an open question is
        // not what the user asked for.
        if (Reassign.IsOpen)
        {
            Reassign.Close();
            return;
        }

        // Read before closing, which is what clears them.
        (int kept, int leftOut, int moved) = (Kept, LeftOut, Moved);
        int removed = _photosRemoved;

        CloseInspection();

        if (kept + leftOut + moved > 0)
        {
            await SettleAsync(kept, leftOut, moved).ConfigureAwait(true);
        }
        else if (removed > 0)
        {
            // There is no tally to report - nobody was answered - but a
            // photograph leaving the library changes the number beside every
            // name, and the button that offers the queue reads its count from
            // there. Without this it went on promising questions whose pictures
            // no longer exist, and opening it landed on an empty screen.
            await ReloadAsync().ConfigureAwait(true);
            await LeaveEmptyQueueAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task InspectNextAsync() => StepAsync(1);

    [RelayCommand]
    private Task InspectPreviousAsync() => StepAsync(-1);

    /// <summary>Answers the open proposal, then moves on to the next.</summary>
    /// <remarks>
    /// The answer is written straight away and the face leaves the queue, so what
    /// is on screen is always what is still to do. What is not done straight away
    /// is the consequence: every answer changes the person's eras, which changes
    /// what should be proposed, and running that over every face in the library
    /// after each click would make walking a queue of two hundred unusable. It is
    /// done once, on the way out.
    /// </remarks>
    [RelayCommand]
    private Task KeepInspectedAsync() => AnswerAsync(keep: true);

    [RelayCommand]
    private Task DropInspectedAsync() => AnswerAsync(keep: false);

    private async Task AnswerAsync(bool keep)
    {
        if (Inspected is null || SelectedPerson is null)
        {
            return;
        }

        FaceCropItem answered = Inspected;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IPeopleRepository>()
                .AssignAsync(
                    SelectedPerson.Id,
                    [new ScoredFace(answered.FaceId)],
                    keep ? AssignmentSource.Confirmed : AssignmentSource.Rejected)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The face stays in the queue: an answer that was not written is an
            // answer still to give.
            Status = $"That answer could not be saved: {ex.Message}";
            return;
        }

        if (keep)
        {
            Kept++;
        }
        else
        {
            LeftOut++;
        }

        await LeaveQueueAsync(answered).ConfigureAwait(true);
    }

    /// <summary>
    /// Brings the screen up to date with the answers just given.
    /// </summary>
    /// <remarks>
    /// The faces that were answered are already gone from the list - answering
    /// one takes it out as it goes - so what comes back is the rest of the
    /// queue, and the screen it comes back to is the questions rather than the
    /// pictures. What this deliberately does not do is look through the library
    /// again.
    ///
    /// <para>That was tried and it read as nothing having happened: re-proposing
    /// refills the queue to its limit, so answering twelve faces and closing
    /// gave back a list the same length as before. Whether to go looking again
    /// is a decision, and it already has a button - Check everyone - rather than
    /// happening on the way out of a screen.</para>
    /// </remarks>
    private async Task SettleAsync(int kept, int leftOut, int moved)
    {
        Status = $"{Answered(kept, leftOut, moved)}. Choose Check everyone to look through "
                 + "the library again for them.";

        await ReloadAsync().ConfigureAwait(true);
        await LeaveEmptyQueueAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// The tally, naming only the answers actually given.
    /// </summary>
    /// <remarks>
    /// "0 left out" alongside a real number reads as a failure to do something,
    /// so a kind of answer nobody gave is not mentioned at all.
    /// </remarks>
    private static string Answered(int kept, int leftOut, int moved)
    {
        List<string> parts = [];

        if (kept > 0)
        {
            parts.Add($"{kept:N0} kept");
        }

        if (leftOut > 0)
        {
            parts.Add($"{leftOut:N0} left out");
        }

        if (moved > 0)
        {
            parts.Add($"{moved:N0} given to someone else");
        }

        return parts.Count switch
        {
            0 => string.Empty,
            1 => parts[0],
            _ => $"{string.Join(", ", parts[..^1])} and {parts[^1]}",
        };
    }

    /// <summary>
    /// Moves through the queue without wrapping.
    /// </summary>
    /// <remarks>
    /// Stopping at the end rather than going round again is what makes the last
    /// answer look like the last answer. Wrapping would present a face that has
    /// already been dealt with as though it were new work.
    /// </remarks>
    private Task StepAsync(int delta)
    {
        if (Inspected is null)
        {
            return Task.CompletedTask;
        }

        int at = Proposals.IndexOf(Inspected);
        int next = at + delta;

        return at < 0 || next < 0 || next >= Proposals.Count
            ? Task.CompletedTask
            : ShowAsync(Proposals[next]);
    }

    private async Task ShowAsync(FaceCropItem face)
    {
        // The name list was opened about the face being left behind, and it
        // writes onto whichever face is open when a name is finally clicked.
        // Carried over, it put the answer on the next face and left the one it
        // had asked about sitting in the queue unanswered.
        Reassign.Close();

        Inspected = face;
        InspectedPicture = null;
        InspectedDetails = null;

        ImageSource? picture = await Task
            .Run(() => TileImageLoader.LoadPreview(_store, face.Face.ThumbnailName))
            .ConfigureAwait(true);

        // Holding an arrow down starts a decode per press. Only the one still
        // being looked at may paint, or the picture that lands last wins.
        if (ReferenceEquals(Inspected, face))
        {
            InspectedPicture = picture;
        }

        await LoadInspectedDetailsAsync(face).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads what is known about the photograph this face came out of.
    /// </summary>
    /// <remarks>
    /// Which occasion a picture is from, how big it is and when it was taken all
    /// bear on "is this them?", and the same panel answers the same questions on
    /// the viewer and the duplicate comparison.
    /// </remarks>
    private async Task LoadInspectedDetailsAsync(FaceCropItem face)
    {
        PhotoFacts? facts;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            facts = await scope.ServiceProvider
                .GetRequiredService<IAssetRepository>()
                .FindFactsAsync(face.Face.AssetId)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // Logged rather than said. The status line is for answers the user
            // gave; a panel that could not be filled in is the app's problem and
            // putting it there would talk over the tally of what they just did.
            DiagnosticLog.Write($"could not read the details of asset {face.Face.AssetId}", ex);
            return;
        }

        if (facts is not null && ReferenceEquals(Inspected, face))
        {
            InspectedDetails = PhotoDetails.Of(facts);
        }
    }

    /// <summary>
    /// Puts the picture down and forgets the tally.
    /// </summary>
    /// <remarks>
    /// Nothing is settled here. The callers that rebuild the screen are already
    /// doing the work settling would ask for, and asking for it from inside a
    /// reload would set the reload going again.
    /// </remarks>
    private void CloseInspection()
    {
        Reassign.Close();
        Inspected = null;
        InspectedPicture = null;
        InspectedDetails = null;
        Kept = 0;
        LeftOut = 0;
        Moved = 0;
        _photosRemoved = 0;
        TurnNotice = null;
        TurnNoticeTip = null;
    }

    /// <summary>
    /// Opens the name list on the face being looked at, so a wrong guess can be
    /// corrected rather than only refused.
    /// </summary>
    /// <remarks>
    /// The person whose queue this is comes marked, because that is the answer
    /// being changed - and picking them again is a way of saying "yes" that the
    /// user should be able to see is the same thing.
    /// </remarks>
    [RelayCommand]
    private void ReassignInspected()
    {
        if (Inspected is not FaceCropItem face)
        {
            return;
        }

        Reassign.Open(
            [.. Named.Select(person =>
                new PersonDirectoryEntry(person.Id, person.DisplayName, person.Summary.Photos))],
            SelectedPerson?.Id,
            "Who is this really?",
            $"Taken {face.Caption}. Saying who it is teaches the app what they "
            + "looked like then, rather than only that this one was wrong.");
    }

    /// <summary>
    /// Records the face as somebody else and moves on.
    /// </summary>
    /// <remarks>
    /// Written straight to the index rather than through the assignment handler,
    /// for the same reason keeping and leaving out are: re-weighing every face
    /// in the library after each click would make a queue of two hundred
    /// unusable. The consequence is worked out once, on the way out.
    /// </remarks>
    private async Task ReassignInspectedToAsync(string displayName)
    {
        if (Inspected is not FaceCropItem face || string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        int personId;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IPeopleRepository repository =
                scope.ServiceProvider.GetRequiredService<IPeopleRepository>();

            personId = await repository
                .EnsurePersonAsync(displayName.Trim())
                .ConfigureAwait(true);

            await repository
                .AssignAsync(personId, [new ScoredFace(face.FaceId)], AssignmentSource.Confirmed)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"That answer could not be saved: {ex.Message}";
            return;
        }

        // The name given may belong to somebody who did not exist until now.
        LibraryChanged?.Invoke(this, EventArgs.Empty);

        Reassign.Close();

        // The person whose queue this is comes marked in the list so that they
        // can be picked, and picking them means yes. Counting it as a face given
        // to somebody else then reported the opposite of what was said.
        if (personId == SelectedPerson?.Id)
        {
            Kept++;
        }
        else
        {
            Moved++;
        }

        await LeaveQueueAsync(face).ConfigureAwait(true);
    }

    /// <summary>
    /// Turns the photograph the face was found in, and everything drawn from it.
    /// </summary>
    /// <remarks>
    /// The same turn the photo viewer performs. Offered here because this is
    /// where a picture gets looked at hardest, and a face is very difficult to
    /// recognise upside down - which is exactly when the app most needs an
    /// answer.
    ///
    /// <para>The face has moved with the picture, so its outline and its crop
    /// are read again rather than left where they were.</para>
    /// </remarks>
    [RelayCommand]
    private async Task TurnInspectedAsync(string? degrees)
    {
        if (Inspected is not FaceCropItem face || !int.TryParse(degrees, out int turn))
        {
            return;
        }

        TurnedPhoto result;
        IReadOnlyList<FaceOnPhoto> moved;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            result = await scope.ServiceProvider
                .GetRequiredService<TurnPhotoHandler>()
                .HandleAsync(face.Face.ThumbnailName, turn)
                .ConfigureAwait(true);

            if (!result.Turned)
            {
                // Nothing moved. When the folder is away, say so rather than
                // leaving the button looking broken.
                if (result.UnreachableSources.Count > 0)
                {
                    TurnRefusedOutOfReach?.Invoke(this, result.UnreachableSources);
                }

                return;
            }

            moved = await scope.ServiceProvider
                .GetRequiredService<IPeopleReader>()
                .GetFacesOnAsync(face.Face.AssetId)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"That picture could not be turned: {ex.Message}";
            return;
        }

        bool hereOnly = result.CachedOnly > 0 && result.OriginalsTold == 0;
        TurnNotice = hereOnly ? TurnNotices.HereOnly : null;
        TurnNoticeTip = hereOnly ? TurnNotices.HereOnlyTip : null;

        if (moved.FirstOrDefault(candidate => candidate.FaceId == face.FaceId)
            is not FaceOnPhoto turned)
        {
            return;
        }

        // A crop item is built around its bounds, so the turned face is a new
        // one put back in the same place rather than the old one edited.
        var replacement = new FaceCropItem(face.Face with { Bounds = turned.Bounds })
        {
            // Turning the picture says nothing about who is on it, so a crop
            // already clicked to leave it out has to come back left out. A fresh
            // item is chosen by default, which silently ticked it again and sent
            // it into the library with the next "Yes, these are them".
            IsChosen = face.IsChosen,
        };
        int at = Proposals.IndexOf(face);
        if (at >= 0)
        {
            Proposals[at] = replacement;
        }

        await ShowAsync(replacement).ConfigureAwait(true);
        await DecodeAsync([replacement]).ConfigureAwait(true);
    }

    /// <summary>
    /// What deleting the photograph being looked at would cost, or null when
    /// there is nothing to delete.
    /// </summary>
    /// <remarks>
    /// Read before the question is asked, so the confirmation can say which file
    /// and how many names. The view owns the asking, because a modal dialog is a
    /// view's job.
    /// </remarks>
    public async Task<PhotoToRemove?> DescribeInspectedDeletionAsync()
    {
        if (Inspected is not FaceCropItem face)
        {
            return null;
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<RemovePhotoHandler>()
                .DescribeAsync(face.Face.AssetId)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"That picture could not be read: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Catches the review queue up after the photograph on screen has been
    /// deleted.
    /// </summary>
    /// <remarks>
    /// The deleting itself belongs to the shell, which owns the one overlay that
    /// reports it - this is only what the review screen has left to do about it.
    ///
    /// <para>Every face from that picture leaves the queue, not only the one on
    /// screen: a photograph with three people in it can be asked about three
    /// times, and offering a face from a file that no longer exists is a
    /// question with no answer.</para>
    /// </remarks>
    public async Task AfterInspectedDeletedAsync(PhotoRemovalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (Inspected is not FaceCropItem face)
        {
            return;
        }

        if (result.Deleted == 0)
        {
            Status = $"{face.FileName} is still on disk, so nothing was forgotten.";
            return;
        }

        int assetId = face.Face.AssetId;
        _photosRemoved++;
        LibraryChanged?.Invoke(this, EventArgs.Empty);

        foreach (FaceCropItem gone in
            Proposals.Where(item => item.Face.AssetId == assetId && item != face).ToList())
        {
            Proposals.Remove(gone);
        }

        await LeaveQueueAsync(face).ConfigureAwait(true);
    }

    /// <summary>
    /// Takes an answered face out of the queue and opens whatever moved up into
    /// its place.
    /// </summary>
    private async Task LeaveQueueAsync(FaceCropItem answered)
    {
        // The question it was asking has just been answered, so it goes down
        // with the face. Left up over an emptied queue it also swallowed the
        // Close that follows, which put down the list instead of the picture.
        Reassign.Close();

        int at = Proposals.IndexOf(answered);
        if (at >= 0)
        {
            Proposals.RemoveAt(at);
        }

        if (Proposals.Count == 0)
        {
            await CloseInspectAsync().ConfigureAwait(true);
            return;
        }

        // Whatever moved up into its place, or the new last one if it was the
        // end of the queue.
        await ShowAsync(Proposals[Math.Clamp(at, 0, Proposals.Count - 1)]).ConfigureAwait(true);
    }

    /// <summary>
    /// Keeps the outline over the face as the picture is redrawn.
    /// </summary>
    public void LayoutInspected(double areaWidth, double areaHeight)
    {
        _inspectAreaWidth = areaWidth;
        _inspectAreaHeight = areaHeight;

        if (Inspected is not null
            && PictureFit.Of(areaWidth, areaHeight, InspectedPicture) is PictureFit fit)
        {
            Inspected.PlaceWithin(fit);
        }
    }

    /// <summary>
    /// The picture and the size it is drawn at arrive independently, so whichever
    /// lands second places the outline.
    /// </summary>
    partial void OnInspectedPictureChanged(ImageSource? value) =>
        LayoutInspected(_inspectAreaWidth, _inspectAreaHeight);

    /// <summary>
    /// Creates somebody with no faces yet, so they can be pointed out in a
    /// photograph rather than found in a group first.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task AddPersonAsync()
    {
        string name = NewPersonName.Trim();
        if (name.Length == 0)
        {
            Status = "Type a name to add somebody.";
            return;
        }

        IsBusy = true;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IPeopleRepository>()
                .EnsurePersonAsync(name)
                .ConfigureAwait(true);

            NewPersonName = string.Empty;
            Status = $"{name} is on the list. Open a picture and switch Names on to point them out.";
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"Could not add that name: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Forgets somebody entirely.
    /// </summary>
    /// <remarks>
    /// Their faces are not lost - only what was said about them. The faces go
    /// back to being unnamed, which is what makes this safe after a mistake.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ForgetPersonAsync(PersonItem? person)
    {
        if (person is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IPeopleRepository>()
                .RemovePersonAsync(person.Id)
                .ConfigureAwait(true);

            Status = $"{person.DisplayName} was removed. Their faces are unnamed again.";
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"Could not remove that name: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        SelectedPerson = null;
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ClearSelection() => SelectedPerson = null;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ConfirmProposalsAsync() => ResolveProposals(AssignmentSource.Confirmed);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task RejectProposalsAsync() => ResolveProposals(AssignmentSource.Rejected);

    private Task ResolveProposals(AssignmentSource source)
    {
        if (SelectedPerson is null)
        {
            return Task.CompletedTask;
        }

        int[] chosen = [.. Proposals.Where(crop => crop.IsChosen).Select(crop => crop.FaceId)];
        return chosen.Length == 0
            ? Task.CompletedTask
            : AssignAsync(new AssignFacesRequest(chosen, source, PersonId: SelectedPerson.Id));
    }

    private async Task AssignAsync(AssignFacesRequest request)
    {
        IsBusy = true;
        try
        {
            AssignmentResult result;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                AssignFacesHandler handler =
                    scope.ServiceProvider.GetRequiredService<AssignFacesHandler>();

                result = await Task.Run(() => handler.HandleAsync(request)).ConfigureAwait(true);
            }

            Status = result.Summary;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The same failures removing a name can meet, and the same answer:
            // this is the screen's main action, and the index being briefly
            // unreachable should read as a sentence rather than as a crash.
            Status = $"Could not record that: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        // Naming a group can create somebody who did not exist a moment ago.
        LibraryChanged?.Invoke(this, EventArgs.Empty);

        await ReloadAsync().ConfigureAwait(true);
        await RefreshQuestionsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Re-reads the questions on screen after something has changed what there
    /// is to ask.
    /// </summary>
    /// <remarks>
    /// Rebuilding the board keeps the queue on purpose - that is what stops
    /// closing a picture from throwing away the questions still to answer - so
    /// anything that changes which faces are being asked about has to ask for
    /// the new queue itself.
    ///
    /// <para>Without this the answered faces stayed on screen under their old
    /// count while the name beside them had already come down to what was really
    /// left, and pressing the button again only recorded the same answer a
    /// second time, which reads as the button doing nothing.</para>
    /// </remarks>
    private async Task RefreshQuestionsAsync()
    {
        if (!IsConfirming || SelectedPerson is null)
        {
            return;
        }

        await OpenConfirmQueueAsync().ConfigureAwait(true);
        await LeaveEmptyQueueAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Hands back to the person's pictures once the last question is gone.
    /// </summary>
    /// <remarks>
    /// An emptied queue is nowhere to stand: the row of answers hides itself
    /// when there is nothing to answer, so what is left is a heading over a
    /// blank panel and the pictures the work just changed one click away.
    /// </remarks>
    private Task LeaveEmptyQueueAsync() =>
        IsConfirming && Proposals.Count == 0 ? BackToPhotosAsync() : Task.CompletedTask;

    /// <summary>
    /// Called when a pass has changed what there is to look at.
    /// </summary>
    /// <remarks>
    /// Both callers - Check everyone, and a scan that found faces - withdraw
    /// every proposal and make them again, so a queue left open is answering
    /// questions that no longer exist. It is re-read rather than left alone.
    /// </remarks>
    public async Task RefreshAfterDetectionAsync()
    {
        await ReloadAsync().ConfigureAwait(true);
        await RefreshQuestionsAsync().ConfigureAwait(true);
    }
}

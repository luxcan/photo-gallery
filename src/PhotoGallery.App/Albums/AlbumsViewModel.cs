using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.Imaging;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;

namespace PhotoGallery.App.Albums;

/// <summary>
/// The occasions in the library: the ones the app suggests, and the ones the
/// user made.
/// </summary>
/// <remarks>
/// Two lists rather than one, because they answer to different rules. A
/// suggestion is a question - keep it or throw it away - and a rebuild may
/// change it. Something the user made is theirs, and no pass touches it.
///
/// <para>Putting a photograph into an album moves it out of whichever
/// album it was in, because a photograph belongs to one occasion. Moving
/// originals on disk is a separate confirmed action available only after an
/// album is the user's own.</para>
/// </remarks>
public sealed partial class AlbumsViewModel : ObservableObject
{
    /// <summary>How many covers are decoded at once, as elsewhere.</summary>
    private const int DecodeParallelism = 4;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThumbnailStore _store;
    private readonly TileWindow _photos;

    /// <summary>The proposals, as something the viewer can step through.</summary>
    /// <remarks>
    /// The same tile objects the strip is bound to, not copies: an answer given
    /// in the viewer has to be the answer the strip already holds when it closes.
    /// </remarks>
    private readonly TileWindow _suggestionGrid;

    /// <summary>
    /// True while the lists are being rebuilt.
    /// </summary>
    /// <remarks>
    /// A two-way bound list writes a null selection while it is being cleared,
    /// and unguarded that reads as the user having chosen something else - so
    /// the photographs of the album they are looking at would empty
    /// themselves every time the screen refreshed.
    /// </remarks>
    private bool _rebuilding;

    /// <summary>
    /// Which request to open the description panel is the current one.
    /// </summary>
    /// <remarks>
    /// The rule and the two directories are read before the panel opens, and
    /// during that read the user can go back and ask for a different album, or
    /// for a new one. Both fill the same fields, so without this whichever read
    /// finished last would win - and the panel would stand open on one album
    /// holding another album's rule, which Save then writes.
    /// </remarks>
    private int _panelRequest;

    /// <summary>
    /// Which shelf the wall is standing in, as the band last reported it.
    /// </summary>
    /// <remarks>
    /// The id rather than the row, because the row is not the question. The
    /// band's rows are records, and it hands back a new one for the same shelf
    /// every time it is read and again when that shelf's mosaic arrives - so
    /// <see cref="CollectionsViewModel.Open"/> changes while the reader has not
    /// gone anywhere. Reading that as having gone somewhere closed the album
    /// they had open every time anything was saved.
    /// </remarks>
    private int? _openShelf;

    /// <summary>
    /// True while this screen is reading or writing.
    /// </summary>
    /// <remarks>
    /// Every command on the screen is listed below, and that is not tidiness: a
    /// button realised while the screen happens to be busy evaluates CanExecute
    /// once, finds it false, and stays dead for the rest of the session unless
    /// something tells it to ask again. Both buttons on the rule panel were
    /// exactly that until they were added here.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand),
        nameof(DismissCommand), nameof(DeleteCommand),
        nameof(SaveCommand), nameof(SuggestCommand), nameof(EditCommand),
        nameof(StartCreatingCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    /// <summary>Which tab is showing: what the user made, or what the app suggests.</summary>
    /// <remarks>
    /// True to begin with, so the screen opens on the albums the user made.
    /// Those are the ones they named, and the ones a scan never changes; the
    /// proposals are a queue of questions, and a queue of questions is not what
    /// somebody who came here to find their own holiday wants to arrive at.
    ///
    /// <para>Set in the field rather than in a constructor, which also means the
    /// change notification does not run before anything is listening - nothing
    /// is loaded yet at that point, so there is no wall to show and no album to
    /// close.</para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingSuggested), nameof(Showing),
        nameof(HasNone), nameof(EmptyMessage), nameof(ShowingTheBand))]
    private bool _showMine = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected), nameof(SelectedIsProposed),
        nameof(SelectedIsMine), nameof(SelectedName), nameof(ShowingTheStrip),
        nameof(ShowingOneCollection), nameof(ShowingTheBand),
        nameof(PanelOffersProposal), nameof(PanelOffersOriginals))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand), nameof(DismissCommand),
        nameof(DeleteCommand), nameof(EditCommand),
        nameof(SaveCommand), nameof(SuggestCommand))]
    private AlbumItem? _selected;

    /// <summary>The album's name as the panel currently has it.</summary>
    /// <remarks>
    /// Typed over the existing name rather than beside it: the panel has one
    /// Save, and this is one of the things it saves. It had its own Rename
    /// button once, which meant a name typed and then saved was silently
    /// discarded - the rule went in and the name did not.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _editedName = string.Empty;

    /// <summary>Which of the three date questions the rule is asking.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyDay), nameof(IsOneDay), nameof(IsDateRange),
        nameof(RuleProblem), nameof(HasRuleProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private AlbumDateMode _dateMode;

    /// <summary>The one day the rule admits, when it asks for a single day.</summary>
    [ObservableProperty]
    private DateTime? _ruleDay;

    /// <summary>The first day of the range, when it asks for one.</summary>
    /// <remarks>
    /// A date rather than the text of one. Two text boxes were the shape before
    /// the app had a themed picker, and they made "last March" a thing the panel
    /// had to have an opinion about; a picker cannot produce a day that is not
    /// one, so the only question left is whether the pair is the right way
    /// round.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleProblem), nameof(HasRuleProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private DateTime? _ruleFromDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleProblem), nameof(HasRuleProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private DateTime? _ruleToDate;

    /// <summary>What has been typed to narrow the list of people.</summary>
    [ObservableProperty]
    private string _peopleFilter = string.Empty;

    /// <summary>What has been typed to narrow the list of places.</summary>
    [ObservableProperty]
    private string _placesFilter = string.Empty;

    /// <summary>
    /// True while the panel that describes an album is open.
    /// </summary>
    /// <remarks>
    /// The renaming box and the keep-or-throw-away buttons live behind one quiet
    /// Edit button rather than along the top of the album. Laid out on the strip
    /// they shouted at somebody who had only come to look at their photographs -
    /// and looking is what this screen is for.
    ///
    /// <para>It is also what says the fields underneath mean anything. They are
    /// filled as the panel opens and read again by Save, so Save is listed here:
    /// with the panel shut they hold the last album's answers rather than
    /// anybody's.</para>
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isEditing;

    /// <summary>
    /// True while the album being described does not exist yet.
    /// </summary>
    /// <remarks>
    /// A mode of the one panel rather than a second panel. Making an album and
    /// editing one ask the same questions in the same order and refuse the same
    /// answers; two panels meant two copies of that, and they had already drifted
    /// - only one of them offered a Collection, and only one of them said what a
    /// rule was for.
    ///
    /// <para>What the mode changes is small and honest: the title, the word on
    /// the button, and whether the panel offers to move originals or remove an
    /// album that is not there yet.</para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanelTitle), nameof(PanelHint), nameof(SaveLabel),
        nameof(IsExistingAlbum), nameof(PanelOffersProposal), nameof(PanelOffersOriginals))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isNewAlbum;

    /// <summary>Which collection the panel currently says the album is on.</summary>
    [ObservableProperty]
    private CollectionOption _editedCollection = CollectionOption.None;

    public AlbumsViewModel(IServiceScopeFactory scopeFactory, IThumbnailStore store)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _photos = new TileWindow(store);
        _suggestionGrid = new TileWindow(store);
        Collections = new CollectionsViewModel(scopeFactory, store);
        Collections.Changed += OnCollectionsChanged;
        Collections.PropertyChanged += OnCollectionsPropertyChanged;
    }

    /// <summary>The shelves above these albums, and the one that is open.</summary>
    public CollectionsViewModel Collections { get; }

    /// <summary>Raised when the library's albums have changed.</summary>
    public event EventHandler? LibraryChanged;

    /// <summary>
    /// What a look for photographs that fit is doing, while it does it.
    /// </summary>
    /// <remarks>
    /// Raised for the shell to draw, because the overlay belongs to the window
    /// rather than to this screen - the same reason the album file move is
    /// driven from there. It is also why this is an event rather than a call: a
    /// screen that reached up and covered the whole window would be a second
    /// thing able to do that, and there is deliberately one.
    ///
    /// <para><strong>Nothing is raised for the first
    /// <see cref="OverlayAfter"/> of a look.</strong> A press usually answers in
    /// a few milliseconds, and a modal that appears and vanishes in that time is
    /// a flash nobody can read - worse than the silence it was meant to fix. A
    /// look that outlasts it is one somebody is waiting on, and that one says so
    /// for as long as it runs.</para>
    /// </remarks>
    public event EventHandler<SuggestProgress>? Looking;

    /// <summary>That the look has ended, however it ended.</summary>
    public event EventHandler? LookedEnough;

    /// <summary>
    /// How long a look may take before it is worth covering the window for.
    /// </summary>
    /// <remarks>
    /// Measured on the real library: the matching statement answers in about 137
    /// milliseconds on the first press of a session and between one and ten
    /// afterwards. So this is above the ordinary press and well below the point
    /// at which somebody decides the app has stopped listening.
    /// </remarks>
    private static readonly TimeSpan OverlayAfter = TimeSpan.FromMilliseconds(200);

    /// <summary>Everything the app is offering, newest occasion first.</summary>
    public ObservableCollection<AlbumItem> Suggested { get; } = [];

    /// <summary>Everything the user kept or made, wherever it is.</summary>
    /// <remarks>
    /// The whole of it, including the albums standing on a shelf. The wall draws
    /// <see cref="Wall"/> instead - this is what the wall is filtered from, and
    /// what a count of "your albums" has to be taken from.
    /// </remarks>
    public ObservableCollection<AlbumItem> Mine { get; } = [];

    /// <summary>The albums the wall is actually drawing.</summary>
    /// <remarks>
    /// At the top level, the ones on no shelf; inside an open collection, the
    /// ones on that one. A separate list rather than a filtered view because the
    /// cards are records that are replaced as their covers decode, and a live
    /// filter over a list whose items keep being swapped is a scroll position
    /// that jumps while you are reading it.
    /// </remarks>
    public ObservableCollection<AlbumItem> Wall { get; } = [];

    /// <summary>The list the visible tab is showing.</summary>
    public ObservableCollection<AlbumItem> Showing => ShowMine ? Wall : Suggested;

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

    /// <summary>What the viewer walks when a proposal is opened from the strip.</summary>
    public TileWindow SuggestionGrid => _suggestionGrid;

    /// <summary>The rows of pictures the open album is showing.</summary>
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

    /// <summary>
    /// Whether the screen's own strip is showing: the heading, the two tabs and
    /// the two New buttons.
    /// </summary>
    /// <remarks>
    /// Three headers, one at a time, and each of them is the answer to "where am
    /// I". The strip belongs to the library; a collection's header belongs to
    /// one shelf; an open album brings its own. The tabs and New album sit on
    /// the first because they are about the library rather than about whatever
    /// is open in front of it.
    /// </remarks>
    public bool ShowingTheStrip => !HasSelected && !Collections.HasOpen;

    /// <summary>Whether a collection is open, with no album open inside it.</summary>
    public bool ShowingOneCollection => !HasSelected && Collections.HasOpen;

    /// <summary>
    /// Whether the band of collections is drawn above the wall.
    /// </summary>
    /// <remarks>
    /// Only at the top level of the user's own tab, and only once there is a
    /// collection to draw. Inside one there is nothing to choose between; on the
    /// Suggested tab there is nothing to put on a shelf until it is kept; and
    /// with none made, an empty band would be a strip explaining a feature that
    /// is not being used.
    /// </remarks>
    public bool ShowingTheBand =>
        !HasSelected && !Collections.HasOpen && ShowMine && Collections.HasAny;

    public bool SelectedIsProposed => Selected?.IsProposed == true;

    public bool SelectedIsMine => Selected?.IsMine == true;

    public string SelectedName => Selected?.Name ?? string.Empty;

    public bool HasNone => Showing.Count == 0;

    /// <summary>True while the panel is describing an album that already exists.</summary>
    /// <remarks>
    /// What gates the half of the panel that can only act on a row: an album
    /// that has not been made has no originals to move and nothing to remove.
    /// </remarks>
    public bool IsExistingAlbum => !IsNewAlbum;

    public string PanelTitle => IsNewAlbum ? "New album" : "This album";

    public string PanelHint => IsNewAlbum
        ? "Only the name is needed. Everything else is optional, and is what Find photos "
          + "that fit will go looking for afterwards."
        : "Changing the name, the collection or the rule does not touch the originals.";

    public string SaveLabel => IsNewAlbum ? "Create album" : "Save";

    /// <summary>Whether the panel may offer to keep or throw away a proposal.</summary>
    /// <remarks>
    /// Both of these read the open album as well as the mode. An album that does
    /// not exist cannot be kept, thrown away, moved or removed, and the panel
    /// must not offer any of it while it is describing one.
    /// </remarks>
    public bool PanelOffersProposal => IsExistingAlbum && SelectedIsProposed;

    /// <summary>Whether the panel may offer to move originals, or remove it.</summary>
    public bool PanelOffersOriginals => IsExistingAlbum && SelectedIsMine;

    /// <summary>The collections this album may stand on, and the line for none.</summary>
    public ObservableCollection<CollectionOption> CollectionOptions { get; } = [];

    /// <summary>
    /// The people the rule is asking for, as something to take back off.
    /// </summary>
    /// <remarks>
    /// The answer, on screen, rather than counted underneath a list that cannot
    /// show it. Ticking somebody and then typing another name left the first one
    /// chosen and scrolled out of view, and the sentence that used to sit here
    /// ("5 people chosen") existed only because of that - a count is what you
    /// write when you cannot show the thing itself.
    /// </remarks>
    public ObservableCollection<TickChoice> ChosenPeople { get; } = [];

    /// <summary>The places the rule is asking for, on the same terms.</summary>
    public ObservableCollection<TickChoice> ChosenPlaces { get; } = [];

    public bool HasChosenPeople => ChosenPeople.Count > 0;

    public bool HasChosenPlaces => ChosenPlaces.Count > 0;

    /// <summary>
    /// Whether the people box has anything to offer under it.
    /// </summary>
    /// <remarks>
    /// Only while something is typed. A standing list of everybody is what the
    /// panel used to open with, and it was both the tallest thing on the screen
    /// and the reason the chosen names had nowhere to be.
    /// </remarks>
    public bool HasPeopleSuggestions => ShownPeople.Count > 0;

    public bool HasPlaceSuggestions => ShownPlaces.Count > 0;

    /// <summary>How many people the library has put a name to, said in the box.</summary>
    public string PeoplePrompt => People.Count == 1
        ? "Add someone - 1 person named"
        : $"Add someone - {People.Count:N0} people named";

    public string PlacesPrompt => Places.Count == 1
        ? "Add a place - 1 place known"
        : $"Add a place - {Places.Count:N0} places known";

    /// <summary>Everybody who has been named, to build a rule from.</summary>
    /// <remarks>
    /// The whole directory, ticks and all - the rule is read off this rather
    /// than off what the filter box happens to be showing, so narrowing the list
    /// never quietly drops somebody already chosen.
    /// </remarks>
    public ObservableCollection<TickChoice> People { get; } = [];

    /// <summary>The people the filter box is letting through.</summary>
    public ObservableCollection<TickChoice> ShownPeople { get; } = [];

    /// <summary>Every place photographs have been resolved to.</summary>
    public ObservableCollection<TickChoice> Places { get; } = [];

    /// <summary>The places the filter box is letting through.</summary>
    public ObservableCollection<TickChoice> ShownPlaces { get; } = [];

    public bool HasPeopleToPick => People.Count > 0;

    public bool HasPlacesToPick => Places.Count > 0;

    /// <summary>
    /// What the people box says when it is offering nothing.
    /// </summary>
    /// <remarks>
    /// Two answers, because the list under the box empties for two reasons and
    /// only one of them is a mistake. A name the library cannot match is a
    /// caution: a rule can only ask for somebody the library has already put a
    /// name to, and a name typed at this box cannot make one. A name it can
    /// match but is not offering is the opposite - that person is already in
    /// the rule, and the caution would be printed two rows under their own
    /// chip.
    ///
    /// <para>Silence is not the third answer. An empty list under a box that
    /// will not take the name on Enter either reads as "still loading" rather
    /// than as a reply.</para>
    /// </remarks>
    public string PeopleFilterNote
    {
        get
        {
            string wanted = PeopleFilter.Trim();
            if (wanted.Length == 0 || ShownPeople.Count > 0)
            {
                return string.Empty;
            }

            return People.Any(choice => Matches(choice, wanted))
                ? "Every name that matches is already in the rule - they are above the "
                  + "box, and pressing one takes them back out."
                : "Nobody in this library goes by that name. A rule can only ask for "
                  + "somebody whose face you have already named.";
        }
    }

    public bool HasPeopleFilterNote => PeopleFilterNote.Length > 0;

    /// <summary>What the places box says when it is offering nothing.</summary>
    public string PlacesFilterNote
    {
        get
        {
            string wanted = PlacesFilter.Trim();
            if (wanted.Length == 0 || ShownPlaces.Count > 0)
            {
                return string.Empty;
            }

            return Places.Any(choice => Matches(choice, wanted))
                ? "Every place that matches is already in the rule - they are above the "
                  + "box, and pressing one takes it back out."
                : "Nowhere in this library goes by that name. A place comes from the "
                  + "coordinates in a photograph, so a rule can only ask for one a scan "
                  + "has already worked out.";
        }
    }

    public bool HasPlacesFilterNote => PlacesFilterNote.Length > 0;

    public bool IsAnyDay
    {
        get => DateMode == AlbumDateMode.Any;
        set => Choose(value, AlbumDateMode.Any);
    }

    public bool IsOneDay
    {
        get => DateMode == AlbumDateMode.OneDay;
        set => Choose(value, AlbumDateMode.OneDay);
    }

    public bool IsDateRange
    {
        get => DateMode == AlbumDateMode.Range;
        set => Choose(value, AlbumDateMode.Range);
    }

    /// <summary>What a rule that cannot be met says about itself.</summary>
    public string RuleProblem =>
        DateMode == AlbumDateMode.Range
        && RuleFromDate is DateTime from && RuleToDate is DateTime to && to.Date < from.Date
            ? "The last day is before the first one."
            : string.Empty;

    public bool HasRuleProblem => RuleProblem.Length > 0;

    /// <summary>The photographs the rule found, waiting to be kept or refused.</summary>
    public ObservableCollection<GalleryTile> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>
    /// What the suggestion run found, said once.
    /// </summary>
    /// <remarks>
    /// The headline above a strip of photographs, and nothing else: an answer
    /// with no photographs in it has no strip to sit above, so it goes to
    /// Status instead, which is the line this screen already uses to say what
    /// just happened.
    /// </remarks>
    [ObservableProperty]
    private string _suggestionNote = string.Empty;

    /// <summary>What an empty tab says, which differs by tab.</summary>
    /// <summary>
    /// What an empty wall says, which depends on why it is empty.
    /// </summary>
    /// <remarks>
    /// Three empty walls of the user's own, and they are not the same question.
    /// A library with no albums needs to be told how to make one; an open shelf
    /// with nothing on it needs the button that fills it; and a wall that is
    /// empty only because every album is on a shelf must say so, or it reads as
    /// the albums having gone.
    /// </remarks>
    public string EmptyMessage
    {
        get
        {
            if (!ShowMine)
            {
                return "Nothing suggested yet. Scan your folders and the app will group what "
                       + "it finds - a weekend away, a day out - and offer them here.";
            }

            if (Collections.HasOpen)
            {
                return "Nothing on this collection yet. Choose Add albums to tick ones "
                       + "you already have, or New album to make one here.";
            }

            return Mine.Count == 0
                ? "Nothing of your own yet. Choose New album, and say what it is looking for."
                : "Every album you have is on a collection. Open one above to see what is on "
                  + "it, or make an album that is on none.";
        }
    }

    /// <summary>Opens one album on its photographs.</summary>
    /// <remarks>
    /// The screen is two states rather than two panes: a wall of albums, or one
    /// album open. A list beside a grid spends a quarter of the width on names
    /// when the cover is what anybody recognises a holiday by.
    /// </remarks>
    [RelayCommand]
    private void Open(AlbumItem? album) => Selected = album;

    /// <summary>Back to the wall of albums.</summary>
    [RelayCommand]
    private void CloseAlbum()
    {
        ClosePanel();
        ForgetSuggestions();
        Selected = null;
    }

    /// <summary>
    /// Puts the description panel down, and retires whatever it was waiting on.
    /// </summary>
    /// <remarks>
    /// The panel opens only once the rule and the two directories have been
    /// read, and the header behind it is live for the whole of that read - so
    /// there is a window in which the reader can walk away from a panel they
    /// asked for. Shutting it is not enough on its own: the read still lands,
    /// and it is stopped only by the album having moved on. Walking back into
    /// the same album springs the panel open over the photographs, unasked.
    ///
    /// <para>Saving an album does not come through here. Edit can be pressed
    /// while a save is still in flight, and the panel that read is opening is
    /// one the reader has asked for.</para>
    /// </remarks>
    private void ClosePanel()
    {
        Interlocked.Increment(ref _panelRequest);
        IsEditing = false;
    }

    /// <summary>Opens the panel that renames, keeps or throws this one away.</summary>
    /// <remarks>
    /// Opened last, on a rule that has actually been read. Opening it first and
    /// filling it in behind the read looked the same for as long as the read
    /// succeeded; when it failed, the panel stood open on this album showing the
    /// last one's dates, people and places - and Save, which reads those same
    /// fields, wrote the last album's rule onto this one.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasSelected))]
    private async Task EditAsync()
    {
        if (Selected is not AlbumItem album)
        {
            return;
        }

        int request = Interlocked.Increment(ref _panelRequest);
        Status = string.Empty;

        // Still the same album, asked by id rather than by the row itself: a
        // cover finishing its decode replaces the row it lands on, so the album
        // that is open is very often a different object by the time a read
        // started before it comes back.
        if (!await LoadRuleAsync(album.Id, request).ConfigureAwait(true)
            || Selected is not AlbumItem open
            || open.Id != album.Id)
        {
            return;
        }

        IsNewAlbum = false;
        EditedName = open.Name;
        ShowCollections(open.Summary.CollectionId);
        IsEditing = true;
    }

    /// <summary>
    /// Fills the Collection list, and marks the one the album is on.
    /// </summary>
    /// <remarks>
    /// Read off the band the screen has already loaded rather than asked for
    /// again: it is the same handful of rows, and a panel that opens should not
    /// wait on a query the screen behind it has already run.
    /// </remarks>
    private void ShowCollections(int? current)
    {
        CollectionOptions.Clear();
        CollectionOptions.Add(CollectionOption.None);

        foreach (CollectionItem shelf in Collections.All)
        {
            CollectionOptions.Add(new CollectionOption(shelf.Id, shelf.Name));
        }

        EditedCollection =
            CollectionOptions.FirstOrDefault(option => option.Id == (current ?? 0))
            ?? CollectionOption.None;
    }

    /// <summary>Which collection the panel is asking for, or null for none.</summary>
    private int? ChosenCollection => EditedCollection.Id == 0 ? null : EditedCollection.Id;

    /// <summary>Reads one album's rule into the panel.</summary>
    /// <returns>Whether the fields now describe this album's rule.</returns>
    private async Task<bool> LoadRuleAsync(int albumId, int request)
    {
        AlbumRule rule;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            rule = await scope.ServiceProvider
                .GetRequiredService<IAlbumRepository>()
                .GetRuleAsync(albumId)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"The rule could not be read: {ex.Message}";
            return false;
        }

        return await ShowRuleAsync(rule, request).ConfigureAwait(true);
    }

    /// <summary>
    /// Fills the three rule fields, and the two directories they are chosen from.
    /// </summary>
    /// <remarks>
    /// Read when a panel opens rather than when the screen loads: a library with
    /// fifteen people and four hundred places should not pay for either list
    /// until somebody asks.
    ///
    /// <para>Shared by the panel that edits an album's rule and the one that
    /// describes a new album, because the two ask the same three questions - and
    /// a fourth part added to a rule has to reach both, or they quietly disagree
    /// about what an album can be.</para>
    /// </remarks>
    /// <returns>Whether the fields now describe the rule that was asked for.</returns>
    private async Task<bool> ShowRuleAsync(AlbumRule rule, int request)
    {
        IReadOnlyList<PersonDirectoryEntry> people;
        IReadOnlyList<PlaceDirectoryEntry> places;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            people = await scope.ServiceProvider
                .GetRequiredService<IPeopleReader>()
                .GetDirectoryAsync()
                .ConfigureAwait(true);

            places = await scope.ServiceProvider
                .GetRequiredService<IPlaceReader>()
                .GetDirectoryAsync()
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"The people and places could not be read: {ex.Message}";
            return false;
        }

        // Only the request the user is waiting on may fill these in. Going back
        // and asking for another album, or for a new one, starts a second read
        // over the same fields, and whichever finished last would otherwise win.
        if (request != Volatile.Read(ref _panelRequest))
        {
            return false;
        }

        ShowDates(rule);

        People.Clear();
        foreach (PersonDirectoryEntry person in people)
        {
            People.Add(Choice(
                person.Id,
                person.DisplayName,
                person.Photos,
                rule.PersonIds.Contains(person.Id)));
        }

        OnPropertyChanged(nameof(HasPeopleToPick));

        // Said separately from the list it counts: a binding to a property
        // nothing announces is read once, when the panel is first realised, so
        // the number in the box would otherwise be the first one it ever saw for
        // the life of the window.
        OnPropertyChanged(nameof(PeoplePrompt));

        Places.Clear();

        // Exact places only. A rule that admitted a whole country would be
        // a different question, and one nobody has asked for.
        foreach (PlaceDirectoryEntry place in places
            .Where(entry => entry.Filter.Scope == PlaceScope.Place))
        {
            Places.Add(Choice(
                place.Filter.PlaceId,
                place.Name,
                place.Photos,
                rule.PlaceIds.Contains(place.Filter.PlaceId)));
        }

        OnPropertyChanged(nameof(HasPlacesToPick));
        OnPropertyChanged(nameof(PlacesPrompt));

        // Emptied before the refresh below reads them, rather than relying on
        // the change to do it: they are usually already empty, and an assignment
        // that changes nothing raises nothing.
        PeopleFilter = string.Empty;
        PlacesFilter = string.Empty;
        RefreshChosenCounts();
        return true;
    }

    /// <summary>One tickable line, counted the way both lists count.</summary>
    private TickChoice Choice(int id, string name, int photos, bool isChosen)
    {
        var choice = new TickChoice(
            id, name, photos == 1 ? "1 photo" : $"{photos:N0} photos", isChosen);

        // The count beside the list is the only place a tick hidden by the
        // filter still shows, so it has to hear about every one of them.
        choice.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TickChoice.IsChosen))
            {
                RefreshChosenCounts();
            }
        };

        return choice;
    }

    /// <summary>
    /// Puts a tick's answer back on the screen: on the chips, and out of what is
    /// still being offered underneath.
    /// </summary>
    private void RefreshChosenCounts()
    {
        RefreshChosen();
        NarrowPeople(PeopleFilter);
        NarrowPlaces(PlacesFilter);
    }

    /// <summary>What the three rule fields currently say.</summary>
    private AlbumRule TypedRule()
    {
        // One day is stored as a range of one, because that is what it is, and
        // the reader downstream then has a single shape to answer.
        (DateOnly? From, DateOnly? To) days = DateMode switch
        {
            AlbumDateMode.OneDay => (Day(RuleDay), Day(RuleDay)),
            AlbumDateMode.Range => (Day(RuleFromDate), Day(RuleToDate)),
            _ => (null, null),
        };

        return new AlbumRule(
            days.From,
            days.To,
            [.. People.Where(choice => choice.IsChosen).Select(choice => choice.Id)],
            [.. Places.Where(choice => choice.IsChosen).Select(choice => choice.Id)]);
    }

    /// <summary>Puts a stored rule's dates back on the panel that wrote them.</summary>
    private void ShowDates(AlbumRule rule)
    {
        RuleDay = null;
        RuleFromDate = null;
        RuleToDate = null;

        if (rule.From is null && rule.To is null)
        {
            DateMode = AlbumDateMode.Any;
            return;
        }

        if (rule.From is DateOnly only && rule.To == only)
        {
            DateMode = AlbumDateMode.OneDay;
            RuleDay = only.ToDateTime(TimeOnly.MinValue);
            return;
        }

        DateMode = AlbumDateMode.Range;
        RuleFromDate = rule.From?.ToDateTime(TimeOnly.MinValue);
        RuleToDate = rule.To?.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>
    /// Saves everything the edit panel holds: the name and the rule.
    /// </summary>
    /// <remarks>
    /// One button for one panel. The name used to have a Rename button of its
    /// own beside the box, and Save saved only the rule - so typing a new name
    /// and pressing the panel's one obvious button threw the name away without
    /// saying so. Anything the panel can change, this saves.
    ///
    /// <para>The rename is only sent when the name actually changed, which is
    /// not tidiness: <see cref="IAlbumRepository.RenameAsync"/> records
    /// that the name is the user's, and a suggested album whose name has been
    /// claimed is never re-named by a later scan. Saving a rule must not quietly
    /// adopt a name the app chose.</para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync()
    {
        // The guard the button is gated on, said again here the way every other
        // command on this screen says it: what this writes is read off the
        // panel's fields, and with the panel shut those belong to nothing.
        if (!IsEditing)
        {
            return Task.CompletedTask;
        }

        return IsNewAlbum ? MakeAlbumAsync() : UpdateAlbumAsync();
    }

    private async Task UpdateAlbumAsync()
    {
        if (Selected is not AlbumItem album)
        {
            return;
        }

        AlbumRule rule = TypedRule();
        string name = EditedName.Trim();
        bool renaming = !string.Equals(name, album.Name, StringComparison.Ordinal);
        bool reshelving = ChosenCollection != album.Summary.CollectionId;
        AlbumShelfResult shelved = AlbumShelfResult.Nothing;

        IsBusy = true;
        try
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IAlbumRepository albums = scope.ServiceProvider
                    .GetRequiredService<IAlbumRepository>();

                if (renaming)
                {
                    await albums.RenameAsync(album.Id, name).ConfigureAwait(true);
                }

                await albums.SetRuleAsync(album.Id, rule).ConfigureAwait(true);

                if (reshelving)
                {
                    shelved = await scope.ServiceProvider
                        .GetRequiredService<ICollectionRepository>()
                        .SetAlbumCollectionAsync(album.Id, ChosenCollection)
                        .ConfigureAwait(true);
                }
            }

            IsEditing = false;
            Status = Saved(renaming ? $"Saved as \"{name}\"." : "Saved.", rule, shelved);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"That could not be saved: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        // The wall carries the name and reads the shelf, so either one changing
        // means reading it again - and the shell's counts with it.
        if (renaming || reshelving)
        {
            await ReloadAsync().ConfigureAwait(true);
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Makes the album the panel describes, with everything it was told at once.
    /// </summary>
    /// <remarks>
    /// The name, the shelf and the rule are written in one breath, and the album
    /// is then opened, so Find photos that fit is under the hand of somebody who
    /// has just said what the album is for.
    ///
    /// <para>What it does <em>not</em> do is go and find them: a rule can match
    /// hundreds, and which of those belong is a question for the user rather
    /// than a consequence of naming something.</para>
    /// </remarks>
    private async Task MakeAlbumAsync()
    {
        string name = EditedName.Trim();
        AlbumRule rule = TypedRule();
        int? shelf = ChosenCollection;
        int made = 0;

        IsBusy = true;
        try
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IAlbumRepository albums = scope.ServiceProvider
                    .GetRequiredService<IAlbumRepository>();

                made = await albums.CreateAsync(name).ConfigureAwait(true);

                if (rule.IsSomething)
                {
                    await albums.SetRuleAsync(made, rule).ConfigureAwait(true);
                }

                if (shelf is not null)
                {
                    await scope.ServiceProvider
                        .GetRequiredService<ICollectionRepository>()
                        .SetAlbumCollectionAsync(made, shelf)
                        .ConfigureAwait(true);
                }
            }

            IsEditing = false;
            Status = rule.IsSomething
                ? $"\"{name}\" is ready. Choose Find photos that fit to see what matches."
                : $"\"{name}\" is ready. Open a picture and choose Add to an album.";
            ShowMine = true;
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"That album could not be made: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await ReloadAsync().ConfigureAwait(true);

        // After the lists have been read again, or the album just made is not
        // among them to open.
        if (made > 0)
        {
            Selected = Wall.FirstOrDefault(item => item.Id == made)
                       ?? Mine.FirstOrDefault(item => item.Id == made);
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What to say about a save, including what it did that nobody asked about.
    /// </summary>
    /// <remarks>
    /// A suggestion put on a shelf is kept on the way in, and that outlives this
    /// screen: the album leaves the suggestions for good and no later pass may
    /// rewrite it. The tick list says as much about the albums ticked on it, and
    /// this is the other way on to a shelf.
    ///
    /// <para>Said before the shelf the album came off, because a sentence about
    /// what became of the album belongs beside the album rather than after a
    /// clause naming somewhere else.</para>
    /// </remarks>
    private static string Saved(string saved, AlbumRule rule, AlbumShelfResult shelved)
    {
        string said = rule.IsSomething
            ? $"{saved} Choose Find photos that fit to see what matches."
            : $"{saved} This album has no rule, so nothing is looked for.";

        if (shelved.Kept)
        {
            said = $"{said} This album was a suggestion, and is now yours to keep.";
        }

        return shelved.Left is null ? said : $"{said} Taken off \"{shelved.Left}\".";
    }

    /// <summary>
    /// An album may not be saved without a name, whatever else the panel holds.
    /// </summary>
    /// <remarks>
    /// And not at all with the panel shut. Everything Save writes is read off
    /// the panel's fields, and those describe an album only while the panel that
    /// was filled for it is open.
    /// </remarks>
    private bool CanSave() =>
        IsIdle
        && IsEditing
        && (IsNewAlbum || HasSelected)
        && !HasRuleProblem
        && EditedName.Trim().Length > 0;

    /// <summary>Looks for photographs that fit, and offers them.</summary>
    /// <remarks>
    /// Offers, rather than adds. The user keeps the ones they want, and what
    /// they leave behind is refused for this album so the same button does
    /// not hand it back next time.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSuggest))]
    private async Task SuggestAsync()
    {
        if (Selected is not AlbumItem album)
        {
            return;
        }

        IsBusy = true;
        Suggestions.Clear();
        _suggestionGrid.Fill(Array.Empty<GalleryTile>());

        // Whatever this line last said - a save, an earlier run - is not the
        // answer to the press that just happened, and an answer with no
        // photographs in it is written here rather than above a strip that
        // would not be on screen to carry it.
        Status = string.Empty;

        var since = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Off the UI thread, and that is the load-bearing half of this.
            // SQLite's asynchronous methods are synchronous underneath, so every
            // await below used to complete without ever yielding - the window
            // could not repaint, which is what "it looks unresponsive" was. No
            // affordance of any kind can be drawn by a thread that is busy.
            Task<Found> looking = Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();

                IAlbumRepository albums = scope.ServiceProvider
                    .GetRequiredService<IAlbumRepository>();

                IReadOnlyList<int> matched = await albums
                    .SuggestAsync(album.Id)
                    .ConfigureAwait(false);

                if (matched.Count == 0)
                {
                    // Two empty answers, and telling them apart is most of what
                    // the reader needs. An album with no rule was never going to
                    // find anything and the way out is the Edit panel; an album
                    // with a real rule that finds nothing has run into the one
                    // rule nobody asked for, which is that a photograph lives in
                    // one album. Saying only "nothing fits" leaves a person
                    // checking a date they typed correctly.
                    AlbumRule rule = await albums
                        .GetRuleAsync(album.Id)
                        .ConfigureAwait(false);

                    return new Found(matched, rule, null);
                }

                GalleryPage found = await scope.ServiceProvider
                    .GetRequiredService<QueryGalleryHandler>()
                    .HandleAsync(new GalleryQuery(RankedAssetIds: matched))
                    .ConfigureAwait(false);

                return new Found(matched, AlbumRule.None, found);
            });

            // Said only if there is a wait to explain. A look that answers in
            // the time it takes to let go of the mouse says nothing at all.
            if (await Task.WhenAny(looking, Task.Delay(OverlayAfter)).ConfigureAwait(true)
                != looking)
            {
                Looking?.Invoke(this, SuggestProgress.Searching);
            }

            Found result = await looking.ConfigureAwait(true);

            if (result.Page is null)
            {
                Status = result.Rule.IsSomething
                    ? "Nothing new fits this rule. What it matches is already "
                      + "in this album, or in another album you made."
                    : "This album has no rule, so nothing is looked for. Give "
                      + "it one under Edit, or open a picture and choose Add "
                      + "to an album.";

                return;
            }

            GalleryPage page = result.Page;

            foreach (GalleryItem item in page.Items)
            {
                // Chosen to begin with, as a face proposal is: a screenful is
                // accepted with one press and the odd wrong one is switched off.
                var tile = new GalleryTile(item) { IsChosen = true };
                Suggestions.Add(tile);
            }

            // Filled with the very same tiles, so the viewer steps through the
            // proposals in the order the strip shows them.
            _suggestionGrid.Fill(Suggestions);

            SuggestionNote = Suggestions.Count == 1
                ? "1 photograph fits. Keep it, or switch it off and it will not be offered again."
                : $"{Suggestions.Count:N0} photographs fit. Switch off any that do not belong - "
                  + "they will not be offered for this album again.";

            await DecodeSuggestionsAsync(since).ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            // On Status for the same reason the empty answer is: the failure
            // can happen before a single tile exists, and the strip that would
            // have carried the sentence is not on screen yet.
            Status = $"Nothing could be looked for: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasSuggestions));
            LookedEnough?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>What one look found, carried back off the thread that found it.</summary>
    /// <param name="Rule">
    /// Read only when nothing matched, because the two empty answers are told
    /// apart by it and asking for it otherwise is a query for a sentence nobody
    /// will see.
    /// </param>
    private sealed record Found(
        IReadOnlyList<int> Matched, AlbumRule Rule, GalleryPage? Page);

    private bool CanSuggest() => IsIdle && HasSelected;

    /// <summary>
    /// Answers one proposal outright: into the album, or never offered here again.
    /// </summary>
    /// <remarks>
    /// The strip answers with switches and one press at the end, which suits a
    /// screenful judged at a glance. A photograph open at full size is the other
    /// kind of act - one picture, one decision, next please - so this commits
    /// there and then and the proposal leaves the list. Nothing is left half
    /// answered if the viewer is closed in the middle.
    ///
    /// <para>A refusal is written the way the batch answer writes it, which is
    /// now a refusal and nothing else. Both paths used to record one by adding
    /// the photograph and taking it straight back out, and that moved it out of
    /// whatever album it was in on the way past - invisible only while a
    /// photograph already in an album could never be offered at all.</para>
    /// </remarks>
    /// <returns>True when the proposal was answered and has left the list.</returns>
    public async Task<bool> DecideSuggestionAsync(GalleryTile? tile, bool keep)
    {
        if (tile is null
            || Selected is not AlbumItem album
            || !Suggestions.Contains(tile))
        {
            return false;
        }

        int[] one = [tile.Item.Id];

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAlbumRepository albums = scope.ServiceProvider
                .GetRequiredService<IAlbumRepository>();

            if (keep)
            {
                await albums.AddAsync(album.Id, one).ConfigureAwait(true);
            }
            else
            {
                await albums.RefuseAsync(album.Id, one).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            SuggestionNote = $"That one could not be answered: {ex.Message}";
            return false;
        }

        Suggestions.Remove(tile);
        _suggestionGrid.Fill(Suggestions);
        RetellSuggestions();
        OnPropertyChanged(nameof(HasSuggestions));

        return true;
    }

    /// <summary>
    /// Says how many are left, after one has been answered on its own.
    /// </summary>
    /// <remarks>
    /// The note is written once when the proposals arrive, and answering them one
    /// at a time would otherwise leave it claiming two hundred while six are on
    /// screen.
    /// </remarks>
    private void RetellSuggestions() =>
        SuggestionNote = Suggestions.Count switch
        {
            0 => "That is all of them.",
            1 => "1 photograph left. Switch it off if it does not belong.",
            _ => $"{Suggestions.Count:N0} photographs left. Switch off any that do not belong - "
               + "they will not be offered for this album again.",
        };

    /// <summary>
    /// Puts what the one-at-a-time answers changed back on the album's own screen.
    /// </summary>
    /// <remarks>
    /// Deliberately not run per answer: rebuilding the album's grid decodes its
    /// thumbnails, and doing that behind a viewer nobody is looking through is
    /// work that shows up as the next button being slow to respond.
    /// </remarks>
    public async Task SettleAfterDecidingAsync()
    {
        await ReloadAsync().ConfigureAwait(true);

        if (Selected is AlbumItem still)
        {
            await LoadPhotosAsync(still.Id).ConfigureAwait(true);
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps the ones still switched on, and refuses the rest.</summary>
    [RelayCommand]
    private async Task KeepSuggestionsAsync()
    {
        if (Selected is not AlbumItem album)
        {
            return;
        }

        int[] keeping = [.. Suggestions.Where(tile => tile.IsChosen).Select(tile => tile.Item.Id)];
        int[] refusing = [.. Suggestions.Where(tile => !tile.IsChosen).Select(tile => tile.Item.Id)];

        IsBusy = true;
        try
        {
            AlbumAddResult kept = AlbumAddResult.Nothing;

            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IAlbumRepository albums = scope.ServiceProvider
                    .GetRequiredService<IAlbumRepository>();

                if (keeping.Length > 0)
                {
                    kept = await albums.AddAsync(album.Id, keeping).ConfigureAwait(true);
                }

                if (refusing.Length > 0)
                {
                    // Refused without ever having been in it, and without being
                    // moved out of wherever it is now: switching one off here
                    // answers a question about this album and no other.
                    await albums.RefuseAsync(album.Id, refusing).ConfigureAwait(true);
                }
            }

            Suggestions.Clear();
            _suggestionGrid.Fill(Array.Empty<GalleryTile>());
            SuggestionNote = string.Empty;
            Status = Kept(keeping.Length, kept);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            SuggestionNote = $"They could not be added: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasSuggestions));
        }

        await ReloadAsync().ConfigureAwait(true);
        if (Selected is AlbumItem still)
        {
            await LoadPhotosAsync(still.Id).ConfigureAwait(true);
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the suggestions down without deciding anything.</summary>
    [RelayCommand]
    private void ForgetSuggestions()
    {
        Suggestions.Clear();
        _suggestionGrid.Fill(Array.Empty<GalleryTile>());
        SuggestionNote = string.Empty;
        OnPropertyChanged(nameof(HasSuggestions));
    }

    /// <summary>
    /// What to say once some have been kept: how many, and what they left.
    /// </summary>
    /// <remarks>
    /// A photograph is in at most one album, so keeping a suggested one takes
    /// it out of wherever it was - most often out of a suggestion the app made
    /// itself. Nobody asked for that rule, so it is said out loud rather than
    /// applied quietly, which is the reason the gallery says it too when a
    /// photograph is dropped into an album by hand.
    ///
    /// <para>The count is repeated only when the two differ: "12 photographs
    /// added, 12 of them out of Sunday" tells the reader the same thing
    /// twice.</para>
    /// </remarks>
    private static string Kept(int added, AlbumAddResult result)
    {
        if (added == 0)
        {
            return "None added.";
        }

        string count = added == 1 ? "1 photograph" : $"{added:N0} photographs";

        if (result.From.Count == 0)
        {
            return $"{count} added.";
        }

        string from = string.Join(" and ", result.From);

        return result.Moved == added
            ? $"{count} added, out of {from}."
            : $"{count} added, {result.Moved:N0} of them out of {from}.";
    }

    /// <summary>A picked day as the rule stores it, or null when nothing is picked.</summary>
    private static DateOnly? Day(DateTime? picked) =>
        picked is DateTime when ? DateOnly.FromDateTime(when) : null;

    /// <summary>
    /// Answers one of the three date questions.
    /// </summary>
    /// <remarks>
    /// Only a tick counts. The radio group writes false to the two it is leaving
    /// as well as true to the one it is choosing, and acting on the false would
    /// put the mode back to Any halfway through every change.
    /// </remarks>
    private void Choose(bool chosen, AlbumDateMode mode)
    {
        if (chosen)
        {
            DateMode = mode;
        }
    }

    partial void OnPeopleFilterChanged(string value) => NarrowPeople(value);

    partial void OnPlacesFilterChanged(string value) => NarrowPlaces(value);

    /// <summary>
    /// Reads the people box again, and says everything that answers to it.
    /// </summary>
    /// <remarks>
    /// One place, because the list under the box and the line above it are two
    /// views of the same reading. They were narrowed from three call sites with
    /// a different set of announcements each, which is how the line came to
    /// outlive the list it belonged to: taking a chip off hands the name back to
    /// the box, and only the list was told.
    /// </remarks>
    private void NarrowPeople(string typed)
    {
        Narrow(People, ShownPeople, typed);
        OnPropertyChanged(nameof(HasPeopleSuggestions));
        OnPropertyChanged(nameof(PeopleFilterNote));
        OnPropertyChanged(nameof(HasPeopleFilterNote));
    }

    private void NarrowPlaces(string typed)
    {
        Narrow(Places, ShownPlaces, typed);
        OnPropertyChanged(nameof(HasPlaceSuggestions));
        OnPropertyChanged(nameof(PlacesFilterNote));
        OnPropertyChanged(nameof(HasPlacesFilterNote));
    }

    /// <summary>Puts one of the offered names into the rule.</summary>
    /// <remarks>
    /// The box empties afterwards, which closes the list under it and leaves the
    /// new chip as the only thing that changed. Typing a name then has an end -
    /// the name is in the rule and can be seen to be.
    /// </remarks>
    [RelayCommand]
    private void AddPerson(TickChoice? person)
    {
        if (person is null)
        {
            return;
        }

        person.IsChosen = true;
        PeopleFilter = string.Empty;
    }

    [RelayCommand]
    private void AddPlace(TickChoice? place)
    {
        if (place is null)
        {
            return;
        }

        place.IsChosen = true;
        PlacesFilter = string.Empty;
    }

    /// <summary>Takes one back out of the rule, from its chip.</summary>
    [RelayCommand]
    private void DropPerson(TickChoice? person)
    {
        if (person is not null)
        {
            person.IsChosen = false;
        }
    }

    [RelayCommand]
    private void DropPlace(TickChoice? place)
    {
        if (place is not null)
        {
            place.IsChosen = false;
        }
    }

    /// <summary>
    /// Rebuilds the two rows of chips from what is ticked.
    /// </summary>
    /// <remarks>
    /// Driven off the same objects the rule is read from rather than kept beside
    /// them, so the chips cannot disagree with what is saved - the failure the
    /// old count line could not have, and the reason it was a count.
    /// </remarks>
    private void RefreshChosen()
    {
        Restate(People, ChosenPeople);
        Restate(Places, ChosenPlaces);

        OnPropertyChanged(nameof(HasChosenPeople));
        OnPropertyChanged(nameof(HasChosenPlaces));
    }

    private static void Restate(
        IEnumerable<TickChoice> all, ObservableCollection<TickChoice> chosen)
    {
        chosen.Clear();
        foreach (TickChoice choice in all.Where(choice => choice.IsChosen))
        {
            chosen.Add(choice);
        }
    }

    /// <summary>
    /// Takes the name typed into the people box as the answer.
    /// </summary>
    /// <remarks>
    /// The first of what is left, which is the most photographed of them - the
    /// directory arrives in that order, and after two or three letters that is
    /// almost always the one meant.
    ///
    /// <para>The box is emptied afterwards on purpose: the whole list comes
    /// back, with the new tick on it. Typing a name then has an end - the name
    /// is in the rule and can be seen to be - rather than leaving somebody
    /// looking at a filtered list wondering whether it took.</para>
    /// </remarks>
    [RelayCommand]
    private void ChoosePerson()
    {
        if (PeopleFilter.Trim().Length > 0 && ShownPeople.FirstOrDefault() is TickChoice person)
        {
            person.IsChosen = true;
            PeopleFilter = string.Empty;
        }
    }

    /// <summary>Takes the name typed into the places box as the answer.</summary>
    [RelayCommand]
    private void ChoosePlace()
    {
        if (PlacesFilter.Trim().Length > 0 && ShownPlaces.FirstOrDefault() is TickChoice place)
        {
            place.IsChosen = true;
            PlacesFilter = string.Empty;
        }
    }

    /// <summary>
    /// Puts under the box the ones whose name contains what was typed, and are
    /// not already in the rule.
    /// </summary>
    /// <remarks>
    /// Nothing while the box is empty, which is the whole difference between a
    /// box that finds something and a list that is always there. The ones
    /// already chosen are left out because they are on screen above it, as
    /// chips - offering them again would be offering to do what has been done.
    /// </remarks>
    private static void Narrow(
        IEnumerable<TickChoice> all, ObservableCollection<TickChoice> shown, string typed)
    {
        string wanted = typed.Trim();

        shown.Clear();
        if (wanted.Length == 0)
        {
            return;
        }

        foreach (TickChoice choice in all)
        {
            if (!choice.IsChosen && Matches(choice, wanted))
            {
                shown.Add(choice);
            }
        }
    }

    /// <summary>
    /// Whether one name answers what was typed.
    /// </summary>
    /// <remarks>
    /// One reading of the box, so the list under it and the line above it cannot
    /// disagree about whether the library knows the name at all.
    /// </remarks>
    private static bool Matches(TickChoice choice, string wanted) =>
        choice.Name.Contains(wanted, StringComparison.CurrentCultureIgnoreCase);

    private async Task DecodeSuggestionsAsync(System.Diagnostics.Stopwatch since)
    {
        GalleryTile[] waiting = [.. Suggestions.Where(tile => tile.Picture is null)];
        if (waiting.Length == 0)
        {
            return;
        }

        int ready = 0;

        // The one part of a look that can honestly name a file: a picture at a
        // time, read from the cached copy on this machine. Reported through the
        // same callback that puts each one on screen, so the count and the
        // pictures cannot disagree.
        var arrived = new Progress<(GalleryTile Tile, ImageSource? Picture)>(pair =>
        {
            pair.Tile.Picture = pair.Picture;
            ready++;

            if (since.Elapsed >= OverlayAfter)
            {
                Looking?.Invoke(this, new SuggestProgress(
                    "Getting the photographs ready",
                    pair.Tile.Item.FileName,
                    ready,
                    waiting.Length));
            }
        });

        await Task.Run(() => Parallel.ForEachAsync(
            waiting,
            new ParallelOptions { MaxDegreeOfParallelism = DecodeParallelism },
            (tile, token) =>
            {
                ImageSource? picture = TileImageLoader.LoadTile(_store, tile.ThumbnailName);
                ((IProgress<(GalleryTile, ImageSource?)>)arrived).Report((tile, picture));
                return ValueTask.CompletedTask;
            })).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelEdit() => ClosePanel();

    /// <summary>Reads the albums again, keeping whatever was open open.</summary>
    public async Task ReloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

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

            // The band before the wall. Its counts are of albums and of the
            // photographs in them, so anything that changes an album changes
            // what a shelf says about itself.
            await Collections.ReloadAsync().ConfigureAwait(true);
            Apply(all);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"The albums could not be read: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Forgets a message that belongs to the last time this was open.</summary>
    public void Reopened() => Status = string.Empty;

    /// <summary>Re-reads the open album after its originals changed folders.</summary>
    public async Task SettleAfterOriginalsMovedAsync(string status)
    {
        ClosePanel();
        await ReloadAsync().ConfigureAwait(true);

        // Said before the photographs are read rather than after, so that a
        // read which cannot be done leaves its own line on the screen instead
        // of having the summary of the move written over the top of it.
        Status = status;

        if (Selected is AlbumItem album)
        {
            await LoadPhotosAsync(album.Id).ConfigureAwait(true);
        }
    }

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

    /// <summary>Opens the same panel, for an album that does not exist yet.</summary>
    /// <remarks>
    /// A new album defaults on to whichever collection is open, because that is
    /// where somebody standing inside a shelf pressing New album means to put
    /// it. At the top level it defaults to none.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task StartCreatingAsync()
    {
        int request = Interlocked.Increment(ref _panelRequest);
        Status = string.Empty;

        // Which shelf, read now rather than when the panel opens. The panel
        // opens only after the two directories behind it have been read, and
        // the collection's header - back chevron and all - is live for the whole
        // of that read. Read afterwards, the album would land on whichever shelf
        // happened to be open when the read came back rather than the one the
        // button was pressed in, and it would be written there without a word.
        int? shelf = Collections.Open?.Id;

        // An empty rule, which is also what clears whatever the last time the
        // panel opened left in the fields. The panel opens after it, so a
        // directory that cannot be read leaves no panel rather than one holding
        // the last album's answers for Create to write.
        if (!await ShowRuleAsync(AlbumRule.None, request).ConfigureAwait(true))
        {
            return;
        }

        IsNewAlbum = true;
        EditedName = string.Empty;
        ShowCollections(shelf);
        IsEditing = true;
    }

    private bool CanAnswer() => IsIdle && SelectedIsProposed;

    private bool CanDelete() => IsIdle && SelectedIsMine;

    /// <summary>Does one thing to the open album, then reads the lists again.</summary>
    private async Task AnswerAsync(
        Func<IAlbumRepository, int, Task> answer, string said)
    {
        if (Selected is not AlbumItem album)
        {
            return;
        }

        ClosePanel();
        IsBusy = true;
        try
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                await answer(
                    scope.ServiceProvider.GetRequiredService<IAlbumRepository>(),
                    album.Id).ConfigureAwait(true);
            }

            Status = said;
            EditedName = string.Empty;
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
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

    private void Apply(IReadOnlyList<AlbumSummary> all)
    {
        int wasOpen = Selected?.Id ?? 0;

        _rebuilding = true;
        try
        {
            Suggested.Clear();
            Mine.Clear();

            foreach (AlbumSummary summary in all)
            {
                var item = new AlbumItem(summary, Cover: null);
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

        FillWall();

        // Carried across the rebuild under the same guard as the rebuild, for
        // the same reason a decoded cover is: this is the wall finding the album
        // that is already open, not the user opening a different one. Every row
        // is rebuilt without its cover, so once the open album's cover has
        // decoded this is a real change of row, and unguarded it runs what
        // opening an album runs - the name box refilled from the album over a
        // rename half typed into it, and the photographs read a second time,
        // which puts the reader back at the top of a grid they had scrolled
        // down through. The three paths that do change what is in an album -
        // answering its suggestions, keeping them, and moving its originals -
        // read the photographs again themselves.
        _rebuilding = true;
        try
        {
            Selected = Showing.FirstOrDefault(item => item.Id == wasOpen);
        }
        finally
        {
            _rebuilding = false;
        }

        _ = LoadCoversAsync();
    }

    /// <summary>
    /// Puts on the wall the albums that belong on it: at the top level the ones
    /// on no shelf, and inside an open collection the ones on that one.
    /// </summary>
    /// <remarks>
    /// An album whose shelf this screen has never heard of counts as being on
    /// none. There is no foreign key behind that column - see AlbumConfiguration
    /// for why - so without this rule an album left pointing at a collection
    /// that is gone would be an album that appears on no wall at all, which is
    /// indistinguishable from having lost it.
    /// </remarks>
    private void FillWall()
    {
        int? shelf = Collections.Open?.Id;
        IReadOnlySet<int> known = Collections.KnownIds;

        _rebuilding = true;
        try
        {
            Wall.Clear();

            foreach (AlbumItem item in Mine)
            {
                int? on = item.Summary.CollectionId;
                bool loose = on is null || !known.Contains(on.Value);

                if (shelf is null ? loose : on == shelf)
                {
                    Wall.Add(item);
                }
            }
        }
        finally
        {
            _rebuilding = false;
        }

        OnPropertyChanged(nameof(HasNone));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>
    /// Re-reads the library after a shelf was made, filled, named or removed.
    /// </summary>
    /// <remarks>
    /// One place, because every one of those changes which albums the wall
    /// should be drawing, and because two status lines on one screen is one more
    /// than anybody reads. Opening and closing a shelf comes through here too
    /// and says nothing, which is why an empty sentence leaves the last one
    /// alone rather than clearing it.
    /// </remarks>
    private async void OnCollectionsChanged(object? sender, string said)
    {
        if (said.Length > 0)
        {
            Status = said;
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Going in or out of a shelf redraws the wall, without reading anything.
    /// </summary>
    /// <remarks>
    /// The albums are already in memory and none of them changed - only which of
    /// them belong on the wall did. An album cannot stay open across it: what is
    /// behind the back chevron has moved, and leaving a photograph grid up over a
    /// wall that is no longer the one it came from is how a back button starts
    /// lying.
    /// </remarks>
    private void OnCollectionsPropertyChanged(
        object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // HasAny moves when a collection is made or removed, which is what
        // decides whether there is a band at all.
        if (e.PropertyName == nameof(CollectionsViewModel.HasAny))
        {
            OnPropertyChanged(nameof(ShowingTheBand));
            return;
        }

        if (e.PropertyName != nameof(CollectionsViewModel.Open))
        {
            return;
        }

        // Which shelf, not which row. Only a different shelf is somewhere the
        // reader has gone; a row replaced under them is the band re-reading
        // itself, and nothing on the wall below moved.
        int? shelf = Collections.Open?.Id;
        if (shelf == _openShelf)
        {
            return;
        }

        _openShelf = shelf;

        OnPropertyChanged(nameof(ShowingTheStrip));
        OnPropertyChanged(nameof(ShowingOneCollection));
        OnPropertyChanged(nameof(ShowingTheBand));

        Selected = null;
        FillWall();
    }

    partial void OnSelectedChanged(AlbumItem? value)
    {
        if (_rebuilding)
        {
            return;
        }

        EditedName = value?.Name ?? string.Empty;

        if (value is not null)
        {
            _ = LoadPhotosAsync(value.Id);
        }
    }

    /// <summary>Changing tab shows that tab's wall, with nothing open.</summary>
    /// <remarks>
    /// It opened the first album of whichever tab was arrived at, which is what
    /// a list beside a grid wanted and is wrong for two states: pressing
    /// Suggested asked to see the suggestions, and answered by walking into one
    /// of them. Whichever album happens to be first is not the one anybody meant
    /// to open.
    /// </remarks>
    partial void OnShowMineChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNone));
        Selected = null;
    }

    /// <summary>Opens one album on its photographs, in the order they were taken.</summary>
    /// <remarks>
    /// The guard is here rather than at the callers because the four of them
    /// need two different things from it. Opening an album starts this and does
    /// not wait, so an escape there is an exception nobody is left to observe
    /// and a grid that stays empty with nothing said. The three that do await it
    /// - answering an album's suggestions, keeping them, and moving its
    /// originals - are reached from handlers whose own filters name only the
    /// file exceptions, so a locked or unreachable library went past them and
    /// closed the app.
    /// </remarks>
    private async Task LoadPhotosAsync(int albumId)
    {
        try
        {
            GalleryPage page;

            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IReadOnlyList<int> members = await scope.ServiceProvider
                    .GetRequiredService<IAlbumRepository>()
                    .GetMembersAsync(albumId)
                    .ConfigureAwait(true);

                if (members.Count == 0)
                {
                    _photos.Fill(Array.Empty<GalleryTile>());
                    OnPropertyChanged(nameof(PhotoCount));
                    OnPropertyChanged(nameof(HasPhotos));
                    return;
                }

                // RankedAssetIds already means "these, in this order", which is
                // how a typed description is answered - so an album's grid needs
                // no query of its own.
                page = await scope.ServiceProvider
                    .GetRequiredService<QueryGalleryHandler>()
                    .HandleAsync(new GalleryQuery(RankedAssetIds: members))
                    .ConfigureAwait(true);
            }

            if (Selected?.Id != albumId)
            {
                return;
            }

            _photos.Fill([.. page.Items.Select(item => new GalleryTile(item))]);
            OnPropertyChanged(nameof(PhotoCount));
            OnPropertyChanged(nameof(HasPhotos));

            await _photos.MarkPreparedAsync(CancellationToken.None).ConfigureAwait(true);
            await _photos.ShowRangeAsync(0).ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"The photographs could not be read: {ex.Message}";
        }
    }

    private async Task LoadCoversAsync()
    {
        List<AlbumItem> waiting =
        [
            .. Suggested.Concat(Mine)
                .Where(item => item.Cover is null && item.Summary.CoverThumbnailName is not null),
        ];

        if (waiting.Count == 0)
        {
            return;
        }

        var arrived = new Progress<(AlbumItem Item, ImageSource? Picture)>(pair =>
        {
            Replace(Suggested, pair.Item, pair.Picture);
            Replace(Mine, pair.Item, pair.Picture);
            Replace(Wall, pair.Item, pair.Picture);
        });

        await Task.Run(() => Parallel.ForEachAsync(
            waiting,
            new ParallelOptions { MaxDegreeOfParallelism = DecodeParallelism },
            (item, token) =>
            {
                ImageSource? picture = TileImageLoader.LoadTile(
                    _store, item.Summary.CoverThumbnailName);

                ((IProgress<(AlbumItem, ImageSource?)>)arrived).Report((item, picture));
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
    ///
    /// <para>The carry happens under the same guard as the swap, because it is
    /// the wall re-pointing at the album that is already open rather than the
    /// user opening a different one. Unguarded it runs what opening one runs:
    /// the name box is filled from the album again, over a rename the user is
    /// half way through typing, and the photographs are read a second time,
    /// which clears the grid's rows and loses where they had scrolled to.</para>
    /// </remarks>
    private void Replace(
        ObservableCollection<AlbumItem> list, AlbumItem item, ImageSource? picture)
    {
        int at = list.IndexOf(item);
        if (at < 0)
        {
            return;
        }

        bool wasOpen = Selected == item;
        AlbumItem withCover = item with { Cover = picture };

        _rebuilding = true;
        try
        {
            list[at] = withCover;

            if (wasOpen)
            {
                Selected = withCover;
            }
        }
        finally
        {
            _rebuilding = false;
        }
    }
}

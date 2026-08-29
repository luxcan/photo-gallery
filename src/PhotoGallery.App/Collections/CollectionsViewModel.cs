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
    /// the photographs of the collection they are looking at would empty
    /// themselves every time the screen refreshed.
    /// </remarks>
    private bool _rebuilding;

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
    [NotifyCanExecuteChangedFor(nameof(CreateCollectionCommand), nameof(AcceptCommand),
        nameof(DismissCommand), nameof(RenameCommand), nameof(DeleteCommand),
        nameof(SaveRuleCommand), nameof(SuggestCommand), nameof(EditCommand),
        nameof(StartCreatingCommand))]
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
        nameof(RenameCommand), nameof(DeleteCommand), nameof(EditCommand),
        nameof(SaveRuleCommand), nameof(SuggestCommand))]
    private CollectionItem? _selected;

    /// <summary>The name being typed for a new collection.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCollectionCommand))]
    private string _newName = string.Empty;

    /// <summary>The name being typed over an existing one.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    private string _renameTo = string.Empty;

    /// <summary>Which of the three date questions the rule is asking.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyDay), nameof(IsOneDay), nameof(IsDateRange),
        nameof(RuleProblem), nameof(HasRuleProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveRuleCommand), nameof(CreateCollectionCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(SaveRuleCommand), nameof(CreateCollectionCommand))]
    private DateTime? _ruleFromDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleProblem), nameof(HasRuleProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveRuleCommand), nameof(CreateCollectionCommand))]
    private DateTime? _ruleToDate;

    /// <summary>What has been typed to narrow the list of people.</summary>
    [ObservableProperty]
    private string _peopleFilter = string.Empty;

    /// <summary>What has been typed to narrow the list of places.</summary>
    [ObservableProperty]
    private string _placesFilter = string.Empty;

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

    /// <summary>
    /// True while a new album is being described, before it exists.
    /// </summary>
    /// <remarks>
    /// The same three questions the rule panel asks, asked before the album is
    /// made rather than after. Naming an album and saying what it is for is one
    /// thought, and splitting it in two made the second half easy never to do -
    /// leaving an album that Find photos that fit can say nothing about.
    /// </remarks>
    [ObservableProperty]
    private bool _isCreating;

    public CollectionsViewModel(IServiceScopeFactory scopeFactory, IThumbnailStore store)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _photos = new TileWindow(store);
        _suggestionGrid = new TileWindow(store);
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

    /// <summary>What the viewer walks when a proposal is opened from the strip.</summary>
    public TileWindow SuggestionGrid => _suggestionGrid;

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

    /// <summary>Everybody who has been named, to build a rule from.</summary>
    /// <remarks>
    /// The whole directory, ticks and all - the rule is read off this rather
    /// than off what the filter box happens to be showing, so narrowing the list
    /// never quietly drops somebody already chosen.
    /// </remarks>
    public ObservableCollection<RuleChoice> People { get; } = [];

    /// <summary>The people the filter box is letting through.</summary>
    public ObservableCollection<RuleChoice> ShownPeople { get; } = [];

    /// <summary>Every place photographs have been resolved to.</summary>
    public ObservableCollection<RuleChoice> Places { get; } = [];

    /// <summary>The places the filter box is letting through.</summary>
    public ObservableCollection<RuleChoice> ShownPlaces { get; } = [];

    public bool HasPeopleToPick => People.Count > 0;

    public bool HasPlacesToPick => Places.Count > 0;

    /// <summary>
    /// How many are ticked, said out loud beside each list.
    /// </summary>
    /// <remarks>
    /// Because the filter box can hide one. Ticking Ana, then typing "Ben",
    /// leaves Ana chosen and off the screen, and without this the panel would
    /// look like it was asking for Ben alone.
    /// </remarks>
    public string PeopleChosen => Chosen(People, "person", "people");

    public string PlacesChosen => Chosen(Places, "place", "places");

    /// <summary>
    /// True when what has been typed matches nobody.
    /// </summary>
    /// <remarks>
    /// Said rather than left as an empty list, because a rule can only ask for
    /// somebody the library has already put a name to: a face nobody has named
    /// is not a person yet, and a name typed at this box cannot make it one.
    /// Silence here reads as "still loading" rather than "there is no such
    /// person".
    /// </remarks>
    public bool NobodyByThatName => PeopleFilter.Trim().Length > 0 && ShownPeople.Count == 0;

    /// <summary>True when what has been typed matches nowhere.</summary>
    public bool NowhereByThatName => PlacesFilter.Trim().Length > 0 && ShownPlaces.Count == 0;

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

    /// <summary>What the suggestion run found, said once.</summary>
    [ObservableProperty]
    private string _suggestionNote = string.Empty;

    /// <summary>What an empty tab says, which differs by tab.</summary>
    public string EmptyMessage => ShowMine
        ? "Nothing of your own yet. Choose New album, and say what it is looking for."
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
        ForgetSuggestions();
        Selected = null;
    }

    /// <summary>Opens the panel that renames, keeps or throws this one away.</summary>
    [RelayCommand(CanExecute = nameof(HasSelected))]
    private async Task EditAsync()
    {
        if (Selected is not CollectionItem collection)
        {
            return;
        }

        RenameTo = SelectedName;
        Status = string.Empty;
        IsEditing = true;

        await LoadRuleAsync(collection.Id).ConfigureAwait(true);
    }

    /// <summary>Reads one album's rule into the panel.</summary>
    private async Task LoadRuleAsync(int collectionId)
    {
        CollectionRule rule;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            rule = await scope.ServiceProvider
                .GetRequiredService<ICollectionRepository>()
                .GetRuleAsync(collectionId)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"The rule could not be read: {ex.Message}";
            return;
        }

        await ShowRuleAsync(rule).ConfigureAwait(true);
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
    private async Task ShowRuleAsync(CollectionRule rule)
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
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"The people and places could not be read: {ex.Message}";
            return;
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

        // Both filter boxes start empty, so this is what puts the full lists on
        // the screen as well as what clears the last panel's typing.
        PeopleFilter = string.Empty;
        PlacesFilter = string.Empty;
        Narrow(People, ShownPeople, string.Empty);
        Narrow(Places, ShownPlaces, string.Empty);
        RefreshChosenCounts();
    }

    /// <summary>One tickable line, counted the way both lists count.</summary>
    private RuleChoice Choice(int id, string name, int photos, bool isChosen)
    {
        var choice = new RuleChoice(
            id, name, photos == 1 ? "1 photo" : $"{photos:N0} photos", isChosen);

        // The count beside the list is the only place a tick hidden by the
        // filter still shows, so it has to hear about every one of them.
        choice.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RuleChoice.IsChosen))
            {
                RefreshChosenCounts();
            }
        };

        return choice;
    }

    private void RefreshChosenCounts()
    {
        OnPropertyChanged(nameof(PeopleChosen));
        OnPropertyChanged(nameof(PlacesChosen));
    }

    /// <summary>What the three rule fields currently say.</summary>
    private CollectionRule TypedRule()
    {
        // One day is stored as a range of one, because that is what it is, and
        // the reader downstream then has a single shape to answer.
        (DateOnly? From, DateOnly? To) days = DateMode switch
        {
            AlbumDateMode.OneDay => (Day(RuleDay), Day(RuleDay)),
            AlbumDateMode.Range => (Day(RuleFromDate), Day(RuleToDate)),
            _ => (null, null),
        };

        return new CollectionRule(
            days.From,
            days.To,
            [.. People.Where(choice => choice.IsChosen).Select(choice => choice.Id)],
            [.. Places.Where(choice => choice.IsChosen).Select(choice => choice.Id)]);
    }

    /// <summary>Puts a stored rule's dates back on the panel that wrote them.</summary>
    private void ShowDates(CollectionRule rule)
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

    /// <summary>Saves what the rule asks for.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveRule))]
    private async Task SaveRuleAsync()
    {
        if (Selected is not CollectionItem collection)
        {
            return;
        }

        CollectionRule rule = TypedRule();

        IsBusy = true;
        try
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                await scope.ServiceProvider
                    .GetRequiredService<ICollectionRepository>()
                    .SetRuleAsync(collection.Id, rule)
                    .ConfigureAwait(true);
            }

            IsEditing = false;
            Status = rule.IsSomething
                ? "Saved. Choose Find photos that fit to see what matches."
                : "Saved. This album has no rule, so nothing is looked for.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Status = $"That rule could not be saved: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSaveRule() => IsIdle && HasSelected && !HasRuleProblem;

    /// <summary>Looks for photographs that fit, and offers them.</summary>
    /// <remarks>
    /// Offers, rather than adds. The user keeps the ones they want, and what
    /// they leave behind is refused for this collection so the same button does
    /// not hand it back next time.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSuggest))]
    private async Task SuggestAsync()
    {
        if (Selected is not CollectionItem collection)
        {
            return;
        }

        IsBusy = true;
        Suggestions.Clear();
        _suggestionGrid.Fill(Array.Empty<GalleryTile>());
        try
        {
            IReadOnlyList<int> fitting;
            GalleryPage page;

            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                fitting = await scope.ServiceProvider
                    .GetRequiredService<ICollectionRepository>()
                    .SuggestAsync(collection.Id)
                    .ConfigureAwait(true);

                if (fitting.Count == 0)
                {
                    SuggestionNote = "Nothing else fits this rule.";
                    return;
                }

                page = await scope.ServiceProvider
                    .GetRequiredService<QueryGalleryHandler>()
                    .HandleAsync(new GalleryQuery(RankedAssetIds: fitting))
                    .ConfigureAwait(true);
            }

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

            await DecodeSuggestionsAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            SuggestionNote = $"Nothing could be looked for: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasSuggestions));
        }
    }

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
    /// <para>A refusal is written the way the batch answer writes it: added and
    /// taken straight out, which is the one path that records "never offer this
    /// here again" without inventing a second one.</para>
    /// </remarks>
    /// <returns>True when the proposal was answered and has left the list.</returns>
    public async Task<bool> DecideSuggestionAsync(GalleryTile? tile, bool keep)
    {
        if (tile is null
            || Selected is not CollectionItem collection
            || !Suggestions.Contains(tile))
        {
            return false;
        }

        int[] one = [tile.Item.Id];

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ICollectionRepository collections = scope.ServiceProvider
                .GetRequiredService<ICollectionRepository>();

            await collections.AddAsync(collection.Id, one).ConfigureAwait(true);

            if (!keep)
            {
                await collections.RemoveAsync(collection.Id, one).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
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

        if (Selected is CollectionItem still)
        {
            await LoadPhotosAsync(still.Id).ConfigureAwait(true);
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps the ones still switched on, and refuses the rest.</summary>
    [RelayCommand]
    private async Task KeepSuggestionsAsync()
    {
        if (Selected is not CollectionItem collection)
        {
            return;
        }

        int[] keeping = [.. Suggestions.Where(tile => tile.IsChosen).Select(tile => tile.Item.Id)];
        int[] refusing = [.. Suggestions.Where(tile => !tile.IsChosen).Select(tile => tile.Item.Id)];

        IsBusy = true;
        try
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                ICollectionRepository collections = scope.ServiceProvider
                    .GetRequiredService<ICollectionRepository>();

                if (keeping.Length > 0)
                {
                    await collections.AddAsync(collection.Id, keeping).ConfigureAwait(true);
                }

                if (refusing.Length > 0)
                {
                    // Refused without ever having been in it: added and taken
                    // straight out is the same decision, and this is the one
                    // path that records it without a round trip through
                    // membership.
                    await collections.AddAsync(collection.Id, refusing).ConfigureAwait(true);
                    await collections.RemoveAsync(collection.Id, refusing).ConfigureAwait(true);
                }
            }

            Suggestions.Clear();
            _suggestionGrid.Fill(Array.Empty<GalleryTile>());
            SuggestionNote = string.Empty;
            Status = keeping.Length == 1
                ? "1 photograph added."
                : $"{keeping.Length:N0} photographs added.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            SuggestionNote = $"They could not be added: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasSuggestions));
        }

        await ReloadAsync().ConfigureAwait(true);
        if (Selected is CollectionItem still)
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

    private static string Chosen(IEnumerable<RuleChoice> all, string one, string many)
    {
        int count = all.Count(choice => choice.IsChosen);
        return count switch
        {
            0 => string.Empty,
            1 => $"1 {one} chosen",
            _ => $"{count:N0} {many} chosen",
        };
    }

    partial void OnPeopleFilterChanged(string value)
    {
        Narrow(People, ShownPeople, value);
        OnPropertyChanged(nameof(NobodyByThatName));
    }

    partial void OnPlacesFilterChanged(string value)
    {
        Narrow(Places, ShownPlaces, value);
        OnPropertyChanged(nameof(NowhereByThatName));
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
        if (PeopleFilter.Trim().Length > 0 && ShownPeople.FirstOrDefault() is RuleChoice person)
        {
            person.IsChosen = true;
            PeopleFilter = string.Empty;
        }
    }

    /// <summary>Takes the name typed into the places box as the answer.</summary>
    [RelayCommand]
    private void ChoosePlace()
    {
        if (PlacesFilter.Trim().Length > 0 && ShownPlaces.FirstOrDefault() is RuleChoice place)
        {
            place.IsChosen = true;
            PlacesFilter = string.Empty;
        }
    }

    /// <summary>Puts the ones whose name contains what was typed on the screen.</summary>
    private static void Narrow(
        IEnumerable<RuleChoice> all, ObservableCollection<RuleChoice> shown, string typed)
    {
        string wanted = typed.Trim();

        shown.Clear();
        foreach (RuleChoice choice in all)
        {
            if (wanted.Length == 0
                || choice.Name.Contains(wanted, StringComparison.CurrentCultureIgnoreCase))
            {
                shown.Add(choice);
            }
        }
    }

    private async Task DecodeSuggestionsAsync()
    {
        GalleryTile[] waiting = [.. Suggestions.Where(tile => tile.Picture is null)];
        if (waiting.Length == 0)
        {
            return;
        }

        var arrived = new Progress<(GalleryTile Tile, ImageSource? Picture)>(
            pair => pair.Tile.Picture = pair.Picture);

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
            Status = $"The albums could not be read: {ex.Message}";
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

    /// <summary>Opens the panel that describes an album before making it.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task StartCreatingAsync()
    {
        NewName = string.Empty;
        Status = string.Empty;
        IsCreating = true;

        // An empty rule, which is also what clears whatever the last panel to
        // open left in the fields.
        await ShowRuleAsync(CollectionRule.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelCreate() => IsCreating = false;

    /// <summary>Makes an album of the user's own, with whatever it was told to look for.</summary>
    /// <remarks>
    /// The rule is written in the same breath as the name, and the album is then
    /// opened, so Edit and Find photos that fit are under the hand of somebody
    /// who has just said what the album is for.
    ///
    /// <para>What it does <em>not</em> do is go and find them: a rule can match
    /// hundreds, and which of those belong is a question for the user rather
    /// than a consequence of naming something. The button is one press away on
    /// the album that just opened.</para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateCollectionAsync()
    {
        string name = NewName.Trim();
        CollectionRule rule = TypedRule();
        int created = 0;

        IsBusy = true;
        try
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                ICollectionRepository collections = scope.ServiceProvider
                    .GetRequiredService<ICollectionRepository>();

                created = await collections.CreateAsync(name).ConfigureAwait(true);

                if (rule.IsSomething)
                {
                    await collections.SetRuleAsync(created, rule).ConfigureAwait(true);
                }
            }

            NewName = string.Empty;
            IsCreating = false;
            Status = rule.IsSomething
                ? $"\"{name}\" is ready. Choose Find photos that fit to see what matches."
                : $"\"{name}\" is ready. Open a picture and choose Add to an album.";
            ShowMine = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
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
        if (created > 0)
        {
            Selected = Mine.FirstOrDefault(item => item.Id == created);
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanAnswer() => IsIdle && SelectedIsProposed;

    private bool CanDelete() => IsIdle && SelectedIsMine;

    private bool CanRename() => IsIdle && HasSelected && RenameTo.Trim().Length > 0;

    private bool CanCreate() => IsIdle && NewName.Trim().Length > 0 && !HasRuleProblem;

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

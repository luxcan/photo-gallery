using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Albums;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Describing an album before it exists.
/// </summary>
/// <remarks>
/// Naming an album and saying what it is looking for used to be two separate
/// acts - a box on the strip made the album, and its rule was only reachable
/// afterwards behind Edit. The second half was easy never to do, which left
/// albums that Find photos that fit could say nothing about. The panel now asks
/// both at once, so what these cover is that the rule typed beside the name
/// actually reaches the album that name made.
/// </remarks>
public sealed class NewAlbumTests : IDisposable
{
    private const int Ana = 1;
    private const int Genting = 77;

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly FakeAlbums _repository = new();
    private readonly NamedPeople _people = new();
    private readonly KnownPlaces _places = new();
    private readonly AlbumsViewModel _albums;

    public NewAlbumTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-new-album-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _services = new ServiceCollection()
            .AddSingleton<IAlbumRepository>(_repository)
            .AddSingleton<ICollectionRepository, NoCollections>()
            .AddSingleton<IPeopleReader>(_people)
            .AddSingleton<IPlaceReader>(_places)
            .BuildServiceProvider();

        _albums = new AlbumsViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    [Fact]
    public async Task Opening_AsksTheThreeQuestionsWithNothingAnsweredYet()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        Assert.True(_albums.IsEditing);

        // Any day to begin with, so a new album asks nothing about the date
        // until somebody says it should.
        Assert.True(_albums.IsAnyDay);
        Assert.Null(_albums.RuleDay);
        Assert.Null(_albums.RuleFromDate);
        Assert.Null(_albums.RuleToDate);

        // The directories are read when the panel opens, not when the screen
        // loads - and nothing in them starts ticked.
        Assert.Equal(2, _albums.People.Count);
        Assert.All(_albums.People, choice => Assert.False(choice.IsChosen));

        // Exact places only: the country in the directory is not something an
        // album can be told to look for.
        Assert.Equal(Genting, Assert.Single(_albums.Places).Id);

        // Nothing is offered until something is typed. A standing list of
        // everybody was the tallest thing on the panel, and the reason the names
        // already in the rule had nowhere to be.
        Assert.Empty(_albums.ShownPeople);
        Assert.Empty(_albums.ShownPlaces);
        Assert.Empty(_albums.ChosenPeople);
        Assert.Empty(_albums.ChosenPlaces);
    }

    /// <summary>
    /// The number in each box is read again every time the panel opens.
    /// </summary>
    /// <remarks>
    /// It counts a list that is only filled when a panel opens, and nothing
    /// announced it - so the box read the count once, when it was first drawn,
    /// and naming more faces under People and coming back here still showed the
    /// old number. The second open is the half that failed, which is why this
    /// opens the panel twice, against a directory that grew in between.
    ///
    /// <para>The sentences are collected as they are announced rather than read
    /// off the view model afterwards. Read afterwards they prove nothing: the
    /// property counts the list every time it is asked, so it is right whether
    /// or not anything ever told the box to ask again - which is the bug.</para>
    /// </remarks>
    [Fact]
    public async Task ReopeningThePanel_SaysHowManyNamesAndPlacesThereAreNow()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        // Two people and one exact place, each said the way its own count reads.
        Assert.Equal("Add someone - 2 people named", _albums.PeoplePrompt);
        Assert.Equal("Add a place - 1 place known", _albums.PlacesPrompt);

        _people.Add(new PersonDirectoryEntry(3, "Kesh Nadar", 12));
        _places.Add(new PlaceDirectoryEntry(PlaceFilter.Exactly(78), "Cameron Highlands", 61));

        List<string> people = [];
        List<string> places = [];
        _albums.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AlbumsViewModel.PeoplePrompt))
            {
                people.Add(_albums.PeoplePrompt);
            }
            else if (e.PropertyName == nameof(AlbumsViewModel.PlacesPrompt))
            {
                places.Add(_albums.PlacesPrompt);
            }
        };

        await _albums.StartCreatingCommand.ExecuteAsync(null);

        Assert.Contains("Add someone - 3 people named", people);
        Assert.Contains("Add a place - 2 places known", places);
    }

    /// <summary>
    /// Editing an album says the numbers again on the same terms.
    /// </summary>
    /// <remarks>
    /// The two panels are one panel and both fill their lists in the same place,
    /// so today this could only fail with the create half. It is here because a
    /// change that gave Edit a fill of its own would break this half in silence:
    /// the box would still draw, and the number in it would be whatever it read
    /// the first time.
    /// </remarks>
    [Fact]
    public async Task EditingAnAlbum_SaysHowManyNamesAndPlacesThereAreNow()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Genting, at last";
        await _albums.SaveCommand.ExecuteAsync(null);

        _people.Add(new PersonDirectoryEntry(3, "Kesh Nadar", 12));
        _places.Add(new PlaceDirectoryEntry(PlaceFilter.Exactly(78), "Cameron Highlands", 61));

        List<string> people = [];
        List<string> places = [];
        _albums.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AlbumsViewModel.PeoplePrompt))
            {
                people.Add(_albums.PeoplePrompt);
            }
            else if (e.PropertyName == nameof(AlbumsViewModel.PlacesPrompt))
            {
                places.Add(_albums.PlacesPrompt);
            }
        };

        await _albums.EditCommand.ExecuteAsync(null);

        Assert.True(_albums.IsEditing);
        Assert.Contains("Add someone - 3 people named", people);
        Assert.Contains("Add a place - 2 places known", places);
    }

    [Fact]
    public async Task TypingInTheFilter_NarrowsTheListWithoutLosingATick()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.People.Single(choice => choice.Id == Ana).IsChosen = true;

        _albums.PeopleFilter = "dia";

        // Ana is not in what the box offers - she is already in the rule, and
        // her chip is what says so. The count line this used to read instead
        // existed only because a filtered list could not show her.
        Assert.Equal("Diana", Assert.Single(_albums.ShownPeople).Name);
        Assert.Equal("Ana Lim", Assert.Single(_albums.ChosenPeople).Name);

        _albums.EditedName = "Whoever";
        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Ana, Assert.Single(Assert.Single(_repository.RulesSet).Rule.PersonIds));
    }

    [Fact]
    public async Task TypingANameAndPressingEnter_AddsThemAndClearsTheBox()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        _albums.PeopleFilter = "ana";
        _albums.ChoosePersonCommand.Execute(null);

        Assert.True(_albums.People.Single(choice => choice.Id == Ana).IsChosen);

        // Emptied, which closes the list under it and leaves the new chip as
        // the only thing that changed - rather than a filtered list that looks
        // like nothing happened.
        Assert.Equal(string.Empty, _albums.PeopleFilter);
        Assert.Empty(_albums.ShownPeople);
        Assert.Equal("Ana Lim", Assert.Single(_albums.ChosenPeople).Name);
    }

    [Fact]
    public async Task AddingSomeoneMakesAChipAndClearsTheBox()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        _albums.PeopleFilter = "ana";
        TickChoice offered = _albums.ShownPeople.First();

        _albums.AddPersonCommand.Execute(offered);

        Assert.Same(offered, Assert.Single(_albums.ChosenPeople));
        Assert.True(_albums.HasChosenPeople);
        Assert.Equal(string.Empty, _albums.PeopleFilter);
        Assert.Empty(_albums.ShownPeople);
    }

    /// <summary>The chip is the way back out, which is why it is a button.</summary>
    [Fact]
    public async Task TakingTheChipOffTakesThemOutOfTheRule()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.PeopleFilter = "ana";
        _albums.AddPersonCommand.Execute(_albums.ShownPeople.First());

        TickChoice chip = Assert.Single(_albums.ChosenPeople);
        _albums.DropPersonCommand.Execute(chip);

        Assert.Empty(_albums.ChosenPeople);
        Assert.False(_albums.HasChosenPeople);
        Assert.False(chip.IsChosen);
    }

    /// <summary>
    /// Somebody already in the rule is not offered again by the box.
    /// </summary>
    /// <remarks>
    /// Their chip is above it. Offering them a second time would be offering to
    /// do what has been done - and it is what left the old list unable to show
    /// its own answer.
    /// </remarks>
    [Fact]
    public async Task SomebodyAlreadyInTheRuleIsNotOfferedAgain()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.PeopleFilter = "ana";
        TickChoice first = _albums.ShownPeople.First();
        _albums.AddPersonCommand.Execute(first);

        _albums.PeopleFilter = "ana";

        Assert.DoesNotContain(first, _albums.ShownPeople);
        Assert.Same(first, Assert.Single(_albums.ChosenPeople));
    }

    /// <summary>
    /// The line under the box is about the library, not about the list.
    /// </summary>
    /// <remarks>
    /// Add somebody, then type their name again. The box offers nothing, because
    /// they are already in the rule - and the caution about names nobody has
    /// would be printed two rows under that person's own chip.
    /// </remarks>
    [Fact]
    public async Task TypingTheNameOfSomebodyAlreadyInTheRule_SaysSoRatherThanThatNobodyHasIt()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.PeopleFilter = "dia";
        _albums.AddPersonCommand.Execute(_albums.ShownPeople.First());

        _albums.PeopleFilter = "dia";

        Assert.Empty(_albums.ShownPeople);
        Assert.Equal("Diana", Assert.Single(_albums.ChosenPeople).Name);
        Assert.StartsWith(
            "Every name that matches is already in the rule",
            _albums.PeopleFilterNote,
            StringComparison.Ordinal);

        // And the caution is still there for a name the library really does not
        // have, chip or no chip.
        _albums.PeopleFilter = "Nobody At All";

        Assert.StartsWith(
            "Nobody in this library goes by that name",
            _albums.PeopleFilterNote,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Taking the chip off hands the name back to the box, and the line above it
    /// is told.
    /// </summary>
    /// <remarks>
    /// A property assertion alone cannot catch this: the getter recomputes, so
    /// it is the notification that was missing. Dropping a chip re-ran the
    /// filter and announced only the list, which left the line standing over the
    /// very name it had just handed back.
    /// </remarks>
    [Fact]
    public async Task TakingTheChipOffWhileTheirNameIsTyped_TakesTheLineWithIt()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.PeopleFilter = "dia";
        _albums.AddPersonCommand.Execute(_albums.ShownPeople.First());
        _albums.PeopleFilter = "dia";

        List<string> announced = [];
        _albums.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? string.Empty);

        _albums.DropPersonCommand.Execute(_albums.ChosenPeople.Single());

        Assert.Equal("Diana", Assert.Single(_albums.ShownPeople).Name);
        Assert.Equal(string.Empty, _albums.PeopleFilterNote);
        Assert.Contains(nameof(AlbumsViewModel.PeopleFilterNote), announced);
        Assert.Contains(nameof(AlbumsViewModel.HasPeopleFilterNote), announced);
    }

    [Fact]
    public async Task TypingAPlaceAndPressingEnter_AddsIt()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        _albums.PlacesFilter = "gent";
        _albums.ChoosePlaceCommand.Execute(null);

        Assert.True(_albums.Places.Single(choice => choice.Id == Genting).IsChosen);
        Assert.Equal(string.Empty, _albums.PlacesFilter);
    }

    [Fact]
    public async Task TypingThePlaceAlreadyInTheRule_SaysSoRatherThanThatNowhereHasIt()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.PlacesFilter = "gent";
        _albums.AddPlaceCommand.Execute(_albums.ShownPlaces.First());

        _albums.PlacesFilter = "gent";

        Assert.Empty(_albums.ShownPlaces);
        Assert.StartsWith(
            "Every place that matches is already in the rule",
            _albums.PlacesFilterNote,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The line stands for however many names it is about, not for one.
    /// </summary>
    /// <remarks>
    /// It is printed whenever every match is already in the rule, and "an"
    /// matches both of the names this library has. Written about one person, a
    /// single sentence about "their name" stood over two chips.
    /// </remarks>
    [Fact]
    public async Task TypingSomethingSeveralChosenNamesMatch_IsNotSaidAboutOneOfThem()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        foreach (TickChoice choice in _albums.People)
        {
            choice.IsChosen = true;
        }

        _albums.PeopleFilter = "an";

        Assert.Equal(2, _albums.ChosenPeople.Count);
        Assert.Empty(_albums.ShownPeople);
        Assert.StartsWith(
            "Every name that matches is already in the rule",
            _albums.PeopleFilterNote,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameNobodyHasBeenGiven_SaysSoAndAddsNothing()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        _albums.PeopleFilter = "Nobody At All";

        Assert.True(_albums.HasPeopleFilterNote);
        Assert.StartsWith(
            "Nobody in this library goes by that name",
            _albums.PeopleFilterNote,
            StringComparison.Ordinal);
        Assert.Empty(_albums.ShownPeople);

        // Enter on a name that matches nobody must not tick something else.
        _albums.ChoosePersonCommand.Execute(null);

        Assert.All(_albums.People, choice => Assert.False(choice.IsChosen));
        Assert.Equal("Nobody At All", _albums.PeopleFilter);
    }

    /// <summary>
    /// The line under each box says what the panel says, not a sentence of its
    /// own.
    /// </summary>
    /// <remarks>
    /// Read as text because a binding to a property that no longer exists fails
    /// silently, and this one fails the wrong way round: the Visibility binding
    /// falls back to Visible, so a caution nothing can switch off would be
    /// printed under the box for good.
    /// </remarks>
    [Fact]
    public void TheLineUnderEachBoxSaysWhatThePanelSays()
    {
        string controls = File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));

        Assert.DoesNotContain("NobodyByThatName", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("NowhereByThatName", controls, StringComparison.Ordinal);

        Assert.Contains(
            "Text=\"{Binding PeopleFilterNote}\"", controls, StringComparison.Ordinal);
        Assert.Contains(
            "Visibility=\"{Binding HasPeopleFilterNote,", controls, StringComparison.Ordinal);
        Assert.Contains(
            "Text=\"{Binding PlacesFilterNote}\"", controls, StringComparison.Ordinal);
        Assert.Contains(
            "Visibility=\"{Binding HasPlacesFilterNote,", controls, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyBox_AddsNothingOnEnter()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        _albums.ChoosePersonCommand.Execute(null);

        Assert.False(_albums.HasPeopleFilterNote);
        Assert.All(_albums.People, choice => Assert.False(choice.IsChosen));
    }

    [Fact]
    public async Task TheFilter_IsForgottenWhenThePanelOpensAgain()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.PeopleFilter = "dia";
        Assert.Single(_albums.ShownPeople);

        await _albums.StartCreatingCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, _albums.PeopleFilter);
        Assert.Empty(_albums.ShownPeople);
    }

    /// <summary>
    /// A directory that cannot be read leaves no panel to make an album from.
    /// </summary>
    /// <remarks>
    /// The same panel describes a new album, and it used to open before the two
    /// directories behind it were read. A read that failed left whatever the
    /// album before it had put in the fields, so Create made an album carrying a
    /// rule nobody had typed for it.
    /// </remarks>
    [Fact]
    public async Task PeopleThatCannotBeRead_LeaveNoPanelToMakeAnAlbumFrom()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.People.Single(choice => choice.Id == Ana).IsChosen = true;
        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2019, 3, 20);
        _albums.CancelEditCommand.Execute(null);

        _people.Unreadable = true;
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        Assert.False(_albums.IsEditing);
        Assert.Contains("could not be read", _albums.Status, StringComparison.Ordinal);

        // Nothing to create from, so nothing is created - and certainly not an
        // album carrying the rule typed for the one before it.
        _albums.EditedName = "Whatever comes next";
        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Null(_repository.Created);
        Assert.Empty(_repository.RulesSet);
    }

    [Fact]
    public async Task OneDay_IsStoredAsARangeOfOne()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "That Tuesday";
        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2019, 3, 3);

        await _albums.SaveCommand.ExecuteAsync(null);

        AlbumRule rule = Assert.Single(_repository.RulesSet).Rule;
        Assert.Equal(new DateOnly(2019, 3, 3), rule.From);
        Assert.Equal(new DateOnly(2019, 3, 3), rule.To);
    }

    [Fact]
    public async Task ChoosingAnyDay_ForgetsWhateverWasPicked()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Odds and ends";
        _albums.IsDateRange = true;
        _albums.RuleFromDate = new DateTime(2019, 3, 3);
        _albums.RuleToDate = new DateTime(2019, 3, 5);

        // Changed their mind: the dates are still in the boxes, but the album
        // must not quietly keep asking about them.
        _albums.IsAnyDay = true;

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_repository.RulesSet);
    }

    [Fact]
    public async Task Creating_WritesTheRuleTypedBesideTheName()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        _albums.EditedName = "Genting, at last";
        _albums.IsDateRange = true;
        _albums.RuleFromDate = new DateTime(2019, 3, 3);
        _albums.RuleToDate = new DateTime(2019, 3, 5);
        _albums.People.Single(choice => choice.Id == Ana).IsChosen = true;
        _albums.Places.Single(choice => choice.Id == Genting).IsChosen = true;

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Genting, at last", _repository.Created);

        AlbumRule rule = Assert.Single(_repository.RulesSet).Rule;
        Assert.Equal(new DateOnly(2019, 3, 3), rule.From);
        Assert.Equal(new DateOnly(2019, 3, 5), rule.To);
        Assert.Equal(Ana, Assert.Single(rule.PersonIds));
        Assert.Equal(Genting, Assert.Single(rule.PlaceIds));
    }

    [Fact]
    public async Task Creating_WritesTheRuleAgainstTheAlbumThatWasJustMade()
    {
        // The id comes back from the create, and a rule written against anything
        // else would be silently attached to somebody else's album.
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "A weekend away";
        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2019, 3, 3);

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Equal(_repository.CreatedId, Assert.Single(_repository.RulesSet).AlbumId);
    }

    [Fact]
    public async Task CreatingWithNothingButAName_WritesNoRuleAtAll()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Odds and ends";

        await _albums.SaveCommand.ExecuteAsync(null);

        // Not an empty rule written over the top: an album that asks for nothing
        // and an album never given a rule are the same album, and writing one
        // would cost a round trip to say so.
        Assert.Empty(_repository.RulesSet);
    }

    [Fact]
    public async Task Creating_ClosesThePanelAndOpensTheAlbum()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Genting, at last";

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.False(_albums.IsEditing);

        // Open, so Edit and Find photos that fit are under the hand of somebody
        // who has just said what the album is for. Both are gated on an album
        // being open, and landing back on the wall would have disabled them.
        Assert.True(_albums.HasSelected);
        Assert.Equal("Genting, at last", _albums.SelectedName);
        Assert.True(_albums.SuggestCommand.CanExecute(null));
        Assert.True(_albums.EditCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheLastDayBeforeTheFirst_StopsTheAlbumBeingMade()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Genting, at last";
        _albums.IsDateRange = true;
        _albums.RuleFromDate = new DateTime(2019, 3, 5);
        _albums.RuleToDate = new DateTime(2019, 3, 3);

        Assert.True(_albums.HasRuleProblem);
        Assert.False(_albums.SaveCommand.CanExecute(null));

        // And it comes back the moment the pair is the right way round, rather
        // than staying dead for the rest of the session.
        _albums.RuleToDate = new DateTime(2019, 3, 7);
        Assert.False(_albums.HasRuleProblem);
        Assert.True(_albums.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheSameDayInBothBoxes_IsAllowed()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "That Tuesday";
        _albums.IsDateRange = true;
        _albums.RuleFromDate = new DateTime(2019, 3, 3);
        _albums.RuleToDate = new DateTime(2019, 3, 3);

        Assert.False(_albums.HasRuleProblem);
        Assert.True(_albums.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnAlbumWithNoName_CannotBeMade()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "   ";

        Assert.False(_albums.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_MakesNothing()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Never made";

        _albums.CancelEditCommand.Execute(null);

        Assert.False(_albums.IsEditing);
        Assert.Null(_repository.Created);
    }

    /// <summary>
    /// A run that finds nothing says so on the line the screen is showing.
    /// </summary>
    /// <remarks>
    /// It used to say so in SuggestionNote, whose only binding lives inside the
    /// panel that appears when there are photographs to show - so the one
    /// answer with no photographs in it was written into a collapsed panel, and
    /// pressing the button looked exactly like pressing a button that does not
    /// work. Status is the line that had just said the album was saved, and it
    /// is on screen whether that panel is or not.
    /// </remarks>
    [Fact]
    public async Task AnEmptyAnswerIsSaidWhereTheScreenCanShowIt()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Zoo";
        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2019, 3, 20);
        await _albums.SaveCommand.ExecuteAsync(null);

        await _albums.SuggestCommand.ExecuteAsync(null);

        Assert.True(_albums.HasStatus);
        Assert.Contains("Nothing new fits this rule", _albums.Status, StringComparison.Ordinal);
        Assert.Empty(_albums.SuggestionNote);
    }

    /// <summary>
    /// And an album with no rule is told that, rather than that nothing fits.
    /// </summary>
    /// <remarks>
    /// The two empty answers have nothing to do with each other. One is a rule
    /// that found nothing, which is about the photographs; the other is an
    /// album that was never given anything to look for, which is about the
    /// album - and a person told "nothing fits" will go back and check a date
    /// they typed correctly, or one they never typed at all.
    /// </remarks>
    [Fact]
    public async Task AnAlbumWithNoRuleIsToldThatInsteadOfNothingFits()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Odds and ends";
        await _albums.SaveCommand.ExecuteAsync(null);

        await _albums.SuggestCommand.ExecuteAsync(null);

        Assert.True(_albums.HasStatus);
        Assert.Contains(
            "This album has no rule", _albums.Status, StringComparison.Ordinal);
        Assert.Contains("under Edit", _albums.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// The answer takes the place of the promise that sent the user to it.
    /// </summary>
    /// <remarks>
    /// This is the shape of the original complaint. Saving an album writes
    /// "Choose Find photos that fit to see what matches" on this line; doing
    /// exactly that left the same sentence sitting there, because the answer
    /// went somewhere the window does not show. A promise that survives being
    /// kept reads as a button that did nothing.
    /// </remarks>
    [Fact]
    public async Task TheAnswerTakesThePlaceOfThePromise()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Zoo";
        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2019, 3, 20);
        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Contains("Find photos that fit", _albums.Status, StringComparison.Ordinal);

        await _albums.SuggestCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Find photos that fit", _albums.Status, StringComparison.Ordinal);
        Assert.NotEmpty(_albums.Status);
    }

    /// <summary>
    /// A look that answers at once covers nothing.
    /// </summary>
    /// <remarks>
    /// The press usually answers in a few milliseconds. A modal that appeared
    /// and vanished in that time would be a flash nobody can read, and it would
    /// happen on every single press - which is a worse answer to "it looked
    /// unresponsive" than the silence it replaced.
    /// </remarks>
    [Fact]
    public async Task AQuickLook_SaysNothingAtAll()
    {
        List<SuggestProgress> said = [];
        _albums.Looking += (_, progress) => said.Add(progress);

        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Zoo";
        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2019, 3, 20);
        await _albums.SaveCommand.ExecuteAsync(null);

        await _albums.SuggestCommand.ExecuteAsync(null);

        Assert.Empty(said);
    }

    /// <summary>
    /// And one that keeps somebody waiting says what it is doing.
    /// </summary>
    /// <remarks>
    /// The whole of the complaint: the matching statement runs against twenty
    /// thousand photographs, and until it was moved off the thread that draws
    /// the window, the window could not be drawn while it ran.
    /// </remarks>
    [Fact]
    public async Task ALookThatKeepsSomebodyWaiting_SaysWhatItIsDoing()
    {
        _repository.SuggestTakes = TimeSpan.FromMilliseconds(600);

        List<SuggestProgress> said = [];
        _albums.Looking += (_, progress) => said.Add(progress);

        bool ended = false;
        _albums.LookedEnough += (_, _) => ended = true;

        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.EditedName = "Zoo";
        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2019, 3, 20);
        await _albums.SaveCommand.ExecuteAsync(null);

        await _albums.SuggestCommand.ExecuteAsync(null);

        SuggestProgress first = Assert.Single(said);
        Assert.Equal("Looking for photographs that fit", first.What);

        // Nothing to count while one statement is being answered, so the bar
        // moves rather than filling.
        Assert.False(first.IsCountable);

        Assert.True(ended, "the look must say when it has ended, or the window stays covered");
    }

    public void Dispose()
    {
        _services.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Records what the screen asked for, and hands back what it made.</summary>
    private sealed class FakeAlbums : IAlbumRepository
    {
        private readonly List<AlbumSummary> _made = [];

        public string? Created { get; private set; }

        public int CreatedId { get; private set; }

        public List<(int AlbumId, AlbumRule Rule)> RulesSet { get; } = [];

        public Task<int> CreateAsync(string name, CancellationToken cancellationToken = default)
        {
            Created = name;

            // Not 1: an id that happens to equal a count or an index would hide
            // the very mix-up the rule-target test is watching for.
            CreatedId = 400 + _made.Count;
            _made.Add(new AlbumSummary(
                CreatedId, name, DateTime.UnixEpoch, DateTime.UnixEpoch,
                AlbumKind.Event, AlbumOrigin.Made, 0, CoverThumbnailName: null));

            return Task.FromResult(CreatedId);
        }

        public Task SetRuleAsync(
            int albumId, AlbumRule rule, CancellationToken cancellationToken = default)
        {
            RulesSet.Add((albumId, rule));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AlbumSummary>> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AlbumSummary>>([.. _made]);

        public Task<IReadOnlyList<int>> GetMembersAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>([]);

        public Task<AlbumRule> GetRuleAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                RulesSet.LastOrDefault(set => set.AlbumId == albumId).Rule
                ?? AlbumRule.None);

        public Task<IReadOnlyList<DatedPhoto>> GetCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<int>>> GetRejectionsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> SaveProposalsAsync(
            IReadOnlyList<ProposedAlbum> proposals,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AlbumSummary?> FindForAssetAsync(
            int assetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>How long a look takes, for the tests about waiting.</summary>
        public TimeSpan SuggestTakes { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Nothing fits, which is the one answer a fake can give honestly.
        /// </summary>
        /// <remarks>
        /// Handing back ids would have the screen build tiles for them, and
        /// that needs the gallery handler, a working folder and thumbnails on
        /// disk - a different test with a different subject. The empty answer
        /// is the one this fixture is here for, and it was unreachable while
        /// this threw.
        /// </remarks>
        public async Task<IReadOnlyList<int>> SuggestAsync(
            int albumId, CancellationToken cancellationToken = default)
        {
            if (SuggestTakes > TimeSpan.Zero)
            {
                await Task.Delay(SuggestTakes, cancellationToken).ConfigureAwait(false);
            }

            return [];
        }

        public Task AcceptAsync(int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DismissAsync(int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RenameAsync(
            int albumId, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AlbumAddResult> AddAsync(
            int albumId,
            IReadOnlyList<int> assetIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            int albumId,
            IReadOnlyList<int> assetIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RefuseAsync(
            int albumId,
            IReadOnlyList<int> assetIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SetCoverAsync(
            int albumId, int assetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Two named faces to begin with, and the means to name more.
    /// </summary>
    /// <remarks>
    /// A directory that can grow between two reads, because that is the whole
    /// of what the count under each box is for: naming another face under People
    /// and coming back here has to show the new number rather than the one the
    /// box was first drawn with.
    /// </remarks>
    private sealed class NamedPeople : IPeopleReader
    {
        private readonly List<PersonDirectoryEntry> _named =
        [
            new PersonDirectoryEntry(Ana, "Ana Lim", 120),
            new PersonDirectoryEntry(2, "Diana", 1),
        ];

        /// <summary>True once the directory is to refuse to be read.</summary>
        public bool Unreadable { get; set; }

        /// <summary>Names one more face, as naming one under People would.</summary>
        public void Add(PersonDirectoryEntry person) => _named.Add(person);

        public Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
            CancellationToken cancellationToken = default) =>
            Unreadable
                ? Task.FromException<IReadOnlyList<PersonDirectoryEntry>>(
                    new IOException("the library is busy"))
                : Task.FromResult<IReadOnlyList<PersonDirectoryEntry>>([.. _named]);

        public Task<IReadOnlyList<FaceRecord>> GetFacesAsync(
            bool confirmedOnly, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FaceSample>> GetSamplesAsync(
            int personId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FaceOnPhoto>> GetFacesOnAsync(
            int assetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Person>> GetPeopleAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FaceRejection>> GetRejectionsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A place and the country holding it, so the filtering is exercised rather
    /// than asserted against a list that could only ever have passed.
    /// </summary>
    /// <remarks>
    /// It grows for the same reason the people do: a scan works out where more
    /// photographs were taken, and the count under the box has to be read again
    /// rather than kept from the first time it was drawn.
    /// </remarks>
    private sealed class KnownPlaces : IPlaceReader
    {
        private readonly List<PlaceDirectoryEntry> _known =
        [
            new PlaceDirectoryEntry(PlaceFilter.Exactly(Genting), "Genting", 458),
            new PlaceDirectoryEntry(PlaceFilter.InCountry("MY"), "Malaysia", 900),
        ];

        /// <summary>Works out one more place, as a scan would.</summary>
        public void Add(PlaceDirectoryEntry place) => _known.Add(place);

        public Task<IReadOnlyList<PlaceDirectoryEntry>> GetDirectoryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaceDirectoryEntry>>([.. _known]);
    }
}

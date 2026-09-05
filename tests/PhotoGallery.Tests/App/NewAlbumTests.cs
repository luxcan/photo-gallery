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
            .AddSingleton<IPeopleReader, TwoPeople>()
            .AddSingleton<IPlaceReader, OnePlaceAndOneCountry>()
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
    public async Task ANameNobodyHasBeenGiven_SaysSoAndAddsNothing()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        _albums.PeopleFilter = "Nobody At All";

        Assert.True(_albums.NobodyByThatName);
        Assert.Empty(_albums.ShownPeople);

        // Enter on a name that matches nobody must not tick something else.
        _albums.ChoosePersonCommand.Execute(null);

        Assert.All(_albums.People, choice => Assert.False(choice.IsChosen));
        Assert.Equal("Nobody At All", _albums.PeopleFilter);
    }

    [Fact]
    public async Task AnEmptyBox_AddsNothingOnEnter()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        _albums.ChoosePersonCommand.Execute(null);

        Assert.False(_albums.NobodyByThatName);
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

        public Task<IReadOnlyList<int>> SuggestAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
    }

    private sealed class TwoPeople : IPeopleReader
    {
        public Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersonDirectoryEntry>>(
            [
                new PersonDirectoryEntry(Ana, "Ana Lim", 120),
                new PersonDirectoryEntry(2, "Diana", 1),
            ]);

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
    private sealed class OnePlaceAndOneCountry : IPlaceReader
    {
        public Task<IReadOnlyList<PlaceDirectoryEntry>> GetDirectoryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaceDirectoryEntry>>(
            [
                new PlaceDirectoryEntry(PlaceFilter.Exactly(Genting), "Genting", 458),
                new PlaceDirectoryEntry(PlaceFilter.InCountry("MY"), "Malaysia", 900),
            ]);
    }

}

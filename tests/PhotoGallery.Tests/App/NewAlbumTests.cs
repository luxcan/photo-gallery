using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Collections;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Collections;
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
    private readonly FakeCollections _collections = new();
    private readonly CollectionsViewModel _albums;

    public NewAlbumTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-new-album-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _services = new ServiceCollection()
            .AddSingleton<ICollectionRepository>(_collections)
            .AddSingleton<IPeopleReader, TwoPeople>()
            .AddSingleton<IPlaceReader, OnePlaceAndOneCountry>()
            .BuildServiceProvider();

        _albums = new CollectionsViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    [Fact]
    public async Task Opening_AsksTheThreeQuestionsWithNothingAnsweredYet()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        Assert.True(_albums.IsCreating);

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

        // Both lists start unfiltered, so the panel opens showing everything.
        Assert.Equal(2, _albums.ShownPeople.Count);
        Assert.Single(_albums.ShownPlaces);
    }

    [Fact]
    public async Task TypingInTheFilter_NarrowsTheListWithoutLosingATick()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.People.Single(choice => choice.Id == Ana).IsChosen = true;

        _albums.PeopleFilter = "dia";

        // Ana is off the screen, but she is still in the rule - and the count
        // beside the list is what says so.
        Assert.Equal("Diana", Assert.Single(_albums.ShownPeople).Name);
        Assert.Equal("1 person chosen", _albums.PeopleChosen);

        _albums.NewName = "Whoever";
        await _albums.CreateCollectionCommand.ExecuteAsync(null);

        Assert.Equal(Ana, Assert.Single(Assert.Single(_collections.RulesSet).Rule.PersonIds));
    }

    [Fact]
    public async Task TypingANameAndPressingEnter_AddsThemAndClearsTheBox()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        _albums.PeopleFilter = "ana";
        _albums.ChoosePersonCommand.Execute(null);

        Assert.True(_albums.People.Single(choice => choice.Id == Ana).IsChosen);

        // Emptied, so the whole list comes back with the new tick visible on it
        // rather than leaving a filtered list that looks like nothing happened.
        Assert.Equal(string.Empty, _albums.PeopleFilter);
        Assert.Equal(2, _albums.ShownPeople.Count);
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
        Assert.Equal(2, _albums.ShownPeople.Count);
    }

    [Fact]
    public async Task OneDay_IsStoredAsARangeOfOne()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.NewName = "That Tuesday";
        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2019, 3, 3);

        await _albums.CreateCollectionCommand.ExecuteAsync(null);

        CollectionRule rule = Assert.Single(_collections.RulesSet).Rule;
        Assert.Equal(new DateOnly(2019, 3, 3), rule.From);
        Assert.Equal(new DateOnly(2019, 3, 3), rule.To);
    }

    [Fact]
    public async Task ChoosingAnyDay_ForgetsWhateverWasPicked()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.NewName = "Odds and ends";
        _albums.IsDateRange = true;
        _albums.RuleFromDate = new DateTime(2019, 3, 3);
        _albums.RuleToDate = new DateTime(2019, 3, 5);

        // Changed their mind: the dates are still in the boxes, but the album
        // must not quietly keep asking about them.
        _albums.IsAnyDay = true;

        await _albums.CreateCollectionCommand.ExecuteAsync(null);

        Assert.Empty(_collections.RulesSet);
    }

    [Fact]
    public async Task Creating_WritesTheRuleTypedBesideTheName()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        _albums.NewName = "Genting, at last";
        _albums.IsDateRange = true;
        _albums.RuleFromDate = new DateTime(2019, 3, 3);
        _albums.RuleToDate = new DateTime(2019, 3, 5);
        _albums.People.Single(choice => choice.Id == Ana).IsChosen = true;
        _albums.Places.Single(choice => choice.Id == Genting).IsChosen = true;

        await _albums.CreateCollectionCommand.ExecuteAsync(null);

        Assert.Equal("Genting, at last", _collections.Created);

        CollectionRule rule = Assert.Single(_collections.RulesSet).Rule;
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
        _albums.NewName = "A weekend away";
        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2019, 3, 3);

        await _albums.CreateCollectionCommand.ExecuteAsync(null);

        Assert.Equal(_collections.CreatedId, Assert.Single(_collections.RulesSet).CollectionId);
    }

    [Fact]
    public async Task CreatingWithNothingButAName_WritesNoRuleAtAll()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.NewName = "Odds and ends";

        await _albums.CreateCollectionCommand.ExecuteAsync(null);

        // Not an empty rule written over the top: an album that asks for nothing
        // and an album never given a rule are the same album, and writing one
        // would cost a round trip to say so.
        Assert.Empty(_collections.RulesSet);
    }

    [Fact]
    public async Task Creating_ClosesThePanelAndOpensTheAlbum()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.NewName = "Genting, at last";

        await _albums.CreateCollectionCommand.ExecuteAsync(null);

        Assert.False(_albums.IsCreating);

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
        _albums.NewName = "Genting, at last";
        _albums.IsDateRange = true;
        _albums.RuleFromDate = new DateTime(2019, 3, 5);
        _albums.RuleToDate = new DateTime(2019, 3, 3);

        Assert.True(_albums.HasRuleProblem);
        Assert.False(_albums.CreateCollectionCommand.CanExecute(null));

        // And it comes back the moment the pair is the right way round, rather
        // than staying dead for the rest of the session.
        _albums.RuleToDate = new DateTime(2019, 3, 7);
        Assert.False(_albums.HasRuleProblem);
        Assert.True(_albums.CreateCollectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheSameDayInBothBoxes_IsAllowed()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.NewName = "That Tuesday";
        _albums.IsDateRange = true;
        _albums.RuleFromDate = new DateTime(2019, 3, 3);
        _albums.RuleToDate = new DateTime(2019, 3, 3);

        Assert.False(_albums.HasRuleProblem);
        Assert.True(_albums.CreateCollectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnAlbumWithNoName_CannotBeMade()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.NewName = "   ";

        Assert.False(_albums.CreateCollectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_MakesNothing()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);
        _albums.NewName = "Never made";

        _albums.CancelCreateCommand.Execute(null);

        Assert.False(_albums.IsCreating);
        Assert.Null(_collections.Created);
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
    private sealed class FakeCollections : ICollectionRepository
    {
        private readonly List<CollectionSummary> _made = [];

        public string? Created { get; private set; }

        public int CreatedId { get; private set; }

        public List<(int CollectionId, CollectionRule Rule)> RulesSet { get; } = [];

        public Task<int> CreateAsync(string name, CancellationToken cancellationToken = default)
        {
            Created = name;

            // Not 1: an id that happens to equal a count or an index would hide
            // the very mix-up the rule-target test is watching for.
            CreatedId = 400 + _made.Count;
            _made.Add(new CollectionSummary(
                CreatedId, name, DateTime.UnixEpoch, DateTime.UnixEpoch,
                CollectionKind.Event, CollectionOrigin.Made, 0, CoverThumbnailName: null));

            return Task.FromResult(CreatedId);
        }

        public Task SetRuleAsync(
            int collectionId, CollectionRule rule, CancellationToken cancellationToken = default)
        {
            RulesSet.Add((collectionId, rule));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CollectionSummary>> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollectionSummary>>([.. _made]);

        public Task<IReadOnlyList<int>> GetMembersAsync(
            int collectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>([]);

        public Task<CollectionRule> GetRuleAsync(
            int collectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                RulesSet.LastOrDefault(set => set.CollectionId == collectionId).Rule
                ?? CollectionRule.None);

        public Task<IReadOnlyList<DatedPhoto>> GetCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<int>>> GetRejectionsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> SaveProposalsAsync(
            IReadOnlyList<ProposedCollection> proposals,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CollectionSummary?> FindForAssetAsync(
            int assetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<int>> SuggestAsync(
            int collectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AcceptAsync(int collectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DismissAsync(int collectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RenameAsync(
            int collectionId, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(int collectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CollectionMoveResult> AddAsync(
            int collectionId,
            IReadOnlyList<int> assetIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            int collectionId,
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

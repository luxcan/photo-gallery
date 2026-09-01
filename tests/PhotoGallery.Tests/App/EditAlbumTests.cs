using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Collections;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Places;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Editing an album that already exists: its name and its rule, saved together.
/// </summary>
/// <remarks>
/// The panel asked two questions and had two answers. A Rename button sat beside
/// the name box and Save took only the rule, so typing a new name and pressing
/// the one obvious button at the bottom threw the name away - no error, no
/// mention, the panel simply closed with the old name still on the wall. The
/// user reported it as "it won't update the album name".
///
/// <para>Now there is one Save and it saves everything the panel holds. What
/// these tests mostly pin down is the part that is easy to get wrong on the way
/// there: the rename must be sent <em>only</em> when the name actually changed,
/// because <c>RenameAsync</c> stamps the name as the user's and a suggested
/// album whose name has been claimed is never re-named by a later scan. Saving
/// a rule would otherwise quietly adopt a name the app itself chose.</para>
/// </remarks>
public sealed class EditAlbumTests : IDisposable
{
    private const int Mine = 401;
    private const int Suggested = 402;

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly FakeCollections _collections = new();
    private readonly CollectionsViewModel _albums;

    public EditAlbumTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-edit-album-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _services = new ServiceCollection()
            .AddSingleton<ICollectionRepository>(_collections)
            .AddSingleton<IPeopleReader, NoPeople>()
            .AddSingleton<IPlaceReader, NoPlaces>()
            .BuildServiceProvider();

        _albums = new CollectionsViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    [Fact]
    public async Task Saving_KeepsTheNewNameAsWellAsTheRule()
    {
        // The reported bug: the name was typed, Save was pressed, and only the
        // rule went anywhere.
        await OpenForEditAsync(Mine);

        _albums.EditedName = "BBK Trip 2012";
        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2012, 3, 12);

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Equal((Mine, "BBK Trip 2012"), _collections.Renamed.Single());
        Assert.Equal(Mine, _collections.RulesSet.Single().CollectionId);
        Assert.Equal(new DateOnly(2012, 3, 12), _collections.RulesSet.Single().Rule.From);
    }

    [Fact]
    public async Task Saving_ClosesThePanelAndSaysTheNameItSavedUnder()
    {
        await OpenForEditAsync(Mine);
        _albums.EditedName = "BBK Trip 2012";

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.False(_albums.IsEditing);
        Assert.Contains("BBK Trip 2012", _albums.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingWithoutTouchingTheName_DoesNotRename()
    {
        // Not tidiness. A rename records that the name is the user's, and a
        // suggestion whose name has been claimed is never re-named by a later
        // scan - so saving a rule must not adopt the name the app chose.
        await OpenForEditAsync(Suggested);

        _albums.IsOneDay = true;
        _albums.RuleDay = new DateTime(2012, 3, 12);

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_collections.Renamed);
        Assert.Single(_collections.RulesSet);
    }

    [Fact]
    public async Task SurroundingSpaceIsNotAChangeOfName()
    {
        await OpenForEditAsync(Mine);
        _albums.EditedName = "  BBK Trip  ";

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_collections.Renamed);
    }

    [Fact]
    public async Task AnAlbumCannotBeSavedWithItsNameEmptied()
    {
        // There is no Rename button left to refuse it, so Save has to.
        await OpenForEditAsync(Mine);
        _albums.EditedName = "   ";

        Assert.False(_albums.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpeningTheEditPanel_FillsInTheNameItAlreadyHas()
    {
        await OpenForEditAsync(Mine);

        Assert.True(_albums.IsEditing);
        Assert.Equal("BBK Trip", _albums.EditedName);
    }

    /// <summary>
    /// The panel offers one way to save, and the name box is part of it.
    /// </summary>
    /// <remarks>
    /// Read as text because a WPF binding to a command that no longer exists
    /// fails silently: the button would still draw, and pressing it would do
    /// nothing at all. That is the same class of failure as the bug this
    /// replaced, so it is worth a test that cannot be fooled by a clean build.
    /// </remarks>
    [Fact]
    public void TheEditPanelHasNoSecondWayToSaveTheName()
    {
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        Assert.DoesNotContain("Content=\"Rename\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("RenameCommand", markup, StringComparison.Ordinal);

        Assert.Contains(
            "Text=\"{Binding Collections.EditedName,", markup, StringComparison.Ordinal);
        Assert.Contains(
            "Command=\"{Binding Collections.SaveCommand}\"", markup, StringComparison.Ordinal);
    }

    /// <summary>Loads the wall, opens one album, and opens its edit panel.</summary>
    private async Task OpenForEditAsync(int collectionId)
    {
        await _albums.ReloadAsync();
        _albums.ShowMine = collectionId == Mine;
        _albums.Selected = _albums.Showing.Single(item => item.Id == collectionId);

        await _albums.EditCommand.ExecuteAsync(null);
    }

    public void Dispose()
    {
        _services.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Two albums, and a record of what the panel asked to be done.</summary>
    private sealed class FakeCollections : ICollectionRepository
    {
        public List<(int CollectionId, string Name)> Renamed { get; } = [];

        public List<(int CollectionId, CollectionRule Rule)> RulesSet { get; } = [];

        public Task<IReadOnlyList<CollectionSummary>> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollectionSummary>>(
            [
                new CollectionSummary(
                    Mine, "BBK Trip", DateTime.UnixEpoch, DateTime.UnixEpoch,
                    CollectionKind.Trip, CollectionOrigin.Made, 179, CoverThumbnailName: null),
                new CollectionSummary(
                    Suggested, "12-16 March 2012", DateTime.UnixEpoch, DateTime.UnixEpoch,
                    CollectionKind.Trip, CollectionOrigin.Proposed, 41, CoverThumbnailName: null),
            ]);

        public Task RenameAsync(
            int collectionId, string name, CancellationToken cancellationToken = default)
        {
            Renamed.Add((collectionId, name));
            return Task.CompletedTask;
        }

        public Task SetRuleAsync(
            int collectionId, CollectionRule rule, CancellationToken cancellationToken = default)
        {
            RulesSet.Add((collectionId, rule));
            return Task.CompletedTask;
        }

        public Task<CollectionRule> GetRuleAsync(
            int collectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CollectionRule.None);

        public Task<IReadOnlyList<int>> GetMembersAsync(
            int collectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>([]);

        public Task<int> CreateAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

    private sealed class NoPeople : IPeopleReader
    {
        public Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersonDirectoryEntry>>([]);

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

    private sealed class NoPlaces : IPlaceReader
    {
        public Task<IReadOnlyList<PlaceDirectoryEntry>> GetDirectoryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaceDirectoryEntry>>([]);
    }
}

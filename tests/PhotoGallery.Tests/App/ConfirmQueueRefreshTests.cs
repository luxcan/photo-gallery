using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.People;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What answering a whole batch of questions leaves on the screen.
/// </summary>
/// <remarks>
/// Rebuilding the people board keeps the queue on purpose, so that closing a
/// picture opened from it does not throw away the questions still to answer.
/// That is right for closing a picture and wrong for answering a batch: the
/// faces just answered have stopped being questions. Left alone the queue went
/// on showing them under their old count while the name beside it had already
/// dropped to what was really left - one screen saying twenty-two were waiting
/// and the list saying one - and pressing the button again only recorded the
/// same answer a second time, which reads as the button doing nothing.
/// </remarks>
public sealed class ConfirmQueueRefreshTests : IDisposable
{
    private const int Ana = 1;

    /// <summary>The face deselected before answering, so it stays a question.</summary>
    private const int LeftOut = 12;

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly InMemoryLibrary _library = new();
    private readonly PeopleViewModel _people;

    public ConfirmQueueRefreshTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-queue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _services = new ServiceCollection()
            .AddSingleton<IPeopleReader>(_library)
            .AddSingleton<IPeopleRepository>(_library)
            .AddSingleton<IGalleryReader, NoPicturesToShow>()
            .AddTransient<QueryGalleryHandler>()
            .AddTransient<GetPersonReviewHandler>()
            .AddTransient<GetPeopleBoardHandler>()
            .AddTransient<ProposeFacesHandler>()
            .AddTransient<AssignFacesHandler>()
            .BuildServiceProvider();

        _people = new PeopleViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    [Fact]
    public async Task AnsweringSomeOfThem_LeavesTheRestOfTheQuestionsOnScreen()
    {
        await OpenTheQueueAsync();
        Assert.Equal(3, _people.Proposals.Count);

        // Clicking a crop is what leaves it out; the other two are answered.
        _people.Proposals.Single(crop => crop.FaceId == LeftOut).IsChosen = false;

        await _people.ConfirmProposalsCommand.ExecuteAsync(null);

        Assert.True(_people.IsConfirming, "there is still a question to answer");
        Assert.Equal(LeftOut, Assert.Single(_people.Proposals).FaceId);
    }

    [Fact]
    public async Task TheNameAndTheQueueAgreeOnWhatIsLeft()
    {
        // The symptom this was found by: the badge beside the name had come down
        // to one while the queue next to it still showed every face just
        // answered.
        await OpenTheQueueAsync();
        _people.Proposals.Single(crop => crop.FaceId == LeftOut).IsChosen = false;

        await _people.ConfirmProposalsCommand.ExecuteAsync(null);

        PersonItem ana = _people.Named.Single(person => person.Id == Ana);
        Assert.Equal(ana.AwaitingReview, _people.Proposals.Count);
    }

    [Fact]
    public async Task AnsweringTheLastOfThem_HandsBackToTheirPictures()
    {
        // An empty queue is nowhere to stand: no faces to judge and no buttons
        // under them either, because the row of answers hides itself when there
        // is nothing left to answer.
        await OpenTheQueueAsync();

        await _people.ConfirmProposalsCommand.ExecuteAsync(null);

        Assert.Empty(_people.Proposals);
        Assert.False(_people.IsConfirming);
        Assert.True(_people.ShowingPhotos);
    }

    [Fact]
    public async Task TurningThemAllDown_ClearsTheQuestionsTheSameWay()
    {
        // Rejecting is the same operation with the other answer, and it went
        // wrong in the same place.
        await OpenTheQueueAsync();

        await _people.RejectProposalsCommand.ExecuteAsync(null);

        Assert.Empty(_people.Proposals);
        Assert.False(_people.IsConfirming);
        Assert.DoesNotContain(
            _library.Faces,
            face => face.PersonId == Ana && face.Source == AssignmentSource.Proposed);
    }

    [Fact]
    public async Task LookingThroughTheLibraryAgain_ReplacesTheQuestionsOnScreen()
    {
        // "Check everyone", and a scan that finds faces, both withdraw every
        // proposal in the library and make them again. A queue left standing
        // through that is offering answers to questions that no longer exist,
        // and pressing the button wrote assignments for faces the new round had
        // already routed somewhere else.
        await OpenTheQueueAsync();
        Assert.Equal(3, _people.Proposals.Count);

        _library.WithdrawProposals();

        await _people.RefreshAfterDetectionAsync();

        Assert.Empty(_people.Proposals);
        Assert.False(_people.IsConfirming);
    }

    /// <summary>Selects the person and goes to their questions, as the screen does.</summary>
    private async Task OpenTheQueueAsync()
    {
        await _people.ReloadAsync();
        _people.SelectedPerson = _people.Named.Single(person => person.Id == Ana);

        await _people.OpenConfirmQueueCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// One person, two faces already settled and three still being asked about,
    /// where saying who a face is changes what is read back afterwards.
    /// </summary>
    /// <remarks>
    /// The write side has to be visible to the read side here: what is under
    /// test is whether the screen shows what the library says once an answer has
    /// been recorded, and that cannot be seen through a double which only
    /// remembers what it was told.
    ///
    /// <para>Nobody is given an era, so the proposal sweep that follows every
    /// assignment finds no one to propose for and returns without offering
    /// anything - which is also why withdrawing the outstanding proposals is
    /// counted here rather than carried out. The round therefore leaves exactly
    /// the questions that were not answered, which is what the real sweep does
    /// when nothing else about the library has changed.</para>
    /// </remarks>
    private sealed class InMemoryLibrary : IPeopleReader, IPeopleRepository
    {
        private readonly List<FaceRecord> _faces =
        [
            Face(1, AssignmentSource.Confirmed),
            Face(2, AssignmentSource.Confirmed),
            Face(11, AssignmentSource.Proposed),
            Face(LeftOut, AssignmentSource.Proposed),
            Face(13, AssignmentSource.Proposed),
        ];

        public IReadOnlyList<FaceRecord> Faces => _faces;

        public Task<IReadOnlyList<FaceRecord>> GetFacesAsync(
            bool includeEmbeddings, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceRecord>>([.. _faces]);

        public Task<IReadOnlyList<Person>> GetPeopleAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<Person>>([new Person { Id = Ana, DisplayName = "Ana" }]);

        public Task AssignAsync(
            int personId,
            IReadOnlyList<ScoredFace> faces,
            AssignmentSource source,
            CancellationToken token = default)
        {
            foreach (ScoredFace named in faces)
            {
                int at = _faces.FindIndex(face => face.FaceId == named.FaceId);
                if (at >= 0)
                {
                    _faces[at] = _faces[at] with { PersonId = personId, Source = source };
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Takes back every outstanding proposal, as a fresh sweep over the
        /// library does before it decides them all again.
        /// </summary>
        public void WithdrawProposals()
        {
            for (int at = 0; at < _faces.Count; at++)
            {
                if (_faces[at].Source == AssignmentSource.Proposed)
                {
                    _faces[at] = _faces[at] with { PersonId = null, Source = null };
                }
            }
        }

        /// <summary>Counted rather than carried out - see the remarks above.</summary>
        public int ProposalsCleared { get; private set; }

        public Task ClearProposalsAsync(int personId, CancellationToken token = default)
        {
            ProposalsCleared++;
            return Task.CompletedTask;
        }

        private static FaceRecord Face(int faceId, AssignmentSource source) => new(
            faceId,
            faceId,
            $"face-{faceId}.jpg",
            new FaceBounds(faceId, faceId, 40, 40),
            0.99f,
            new DateTime(2018, 5, 4, 0, 0, 0, DateTimeKind.Utc),
            $@"album\photo-{faceId}.jpg",
            $@"C:\pictures\album\photo-{faceId}.jpg",
            new FaceEmbedding(new float[512]),
            Ana,
            source,
            IsIgnored: false);

        public Task<IReadOnlyList<FaceSample>> GetSamplesAsync(
            int personId, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceSample>>([]);

        public Task<IReadOnlyList<FaceOnPhoto>> GetFacesOnAsync(
            int assetId, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceOnPhoto>>([]);

        public Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<PersonDirectoryEntry>>([]);

        public Task<IReadOnlyList<FaceRejection>> GetRejectionsAsync(
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceRejection>>([]);

        public Task ReplaceErasAsync(
            int personId, IReadOnlyList<PersonEra> eras, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task<int> EnsurePersonAsync(string displayName, CancellationToken token = default) =>
            Task.FromResult(Ana);

        public Task RenamePersonAsync(
            int personId, string displayName, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task SetBirthYearAsync(
            int personId, int? birthYear, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task UnassignAsync(IReadOnlyList<int> faceIds, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task SetIgnoredAsync(
            IReadOnlyList<int> faceIds, bool ignored, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task RemovePersonAsync(int personId, CancellationToken token = default) =>
            Task.CompletedTask;
    }

    /// <summary>A library with no pictures in it, for the one call these reach.</summary>
    /// <remarks>
    /// Handing back to somebody's pictures loads them, and nothing here looks at
    /// that grid. Every other question belongs to a pass and would be a surprise
    /// worth failing on.
    /// </remarks>
    private sealed class NoPicturesToShow : IGalleryReader
    {
        public Task<GalleryPage> QueryAsync(
            GalleryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GalleryPage([], 0));

        public Task<IReadOnlyList<PendingThumbnail>> GetThumbnailCandidatesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PendingVideo>> GetVideoCandidatesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FaceScanCandidate>> GetFaceCandidatesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ContentScanCandidate>> GetContentCandidatesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<LocationCandidate>> GetLocationCandidatesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FolderNode>> GetFoldersAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}

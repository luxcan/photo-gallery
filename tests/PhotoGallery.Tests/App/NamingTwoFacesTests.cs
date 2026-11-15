using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Naming the second face in a photograph, while the first is still saving.
/// </summary>
/// <remarks>
/// Two people in one picture is the ordinary case, and nobody waits between
/// them: you name one, the box changes, you click the other. Confirming a face
/// is not an instant write, though - it reads every copy of the photograph and
/// rebuilds the person - and when it finishes it re-reads the faces on the open
/// picture to settle the boxes the confirmation may have changed. Offering the
/// person to everything else that looks like them, which is seconds on a real
/// library, is no longer part of it: that runs afterwards, in the background.
///
/// <para>That re-read cleared the face being named. By then it was the second
/// face, with its list already up: choosing a name hit the guard at the top of
/// the assignment, returned, and did nothing at all. The list stayed open, the
/// box never changed, and nothing said why - it read as the app having stopped
/// responding. The second name was simply thrown away.</para>
///
/// <para>Written as a race with the timing pinned rather than raced for: the
/// repository below blocks inside the write until the test lets it go, so the
/// second face is always clicked while the first is still saving. Left to
/// chance this test would pass on a fast machine and fail on a slow one, which
/// is the same as not having it.</para>
/// </remarks>
public sealed class NamingTwoFacesTests : IDisposable
{
    private const int FirstFace = 101;
    private const int SecondFace = 102;
    private const int ThePhotograph = 7;
    private const int NextPhotograph = 8;

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly BlockingPeople _repository = new();
    private readonly FileSystemThumbnailStore _thumbnails;
    private readonly GalleryViewModel _gallery;

    public NamingTwoFacesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-two-faces-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _services = new ServiceCollection()
            .AddSingleton<IPeopleReader, TwoFacesOnOnePhoto>()
            .AddSingleton<IPeopleRepository>(_repository)
            .AddTransient<ProposeFacesHandler>()
            .AddTransient<AssignFacesHandler>()
            .AddTransient<GetPeopleBoardHandler>()
            .BuildServiceProvider();

        _thumbnails = new FileSystemThumbnailStore(workingFolder);
        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(), _thumbnails);
    }

    /// <summary>
    /// The bug, stated as a test: the second question survives the first answer.
    /// </summary>
    [Fact]
    public async Task TheSecondFaceIsStillTheOneBeingNamedWhenTheFirstSaveLands()
    {
        await ShowTheFacesAsync();

        _gallery.BeginNamingFaceCommand.Execute(Face(FirstFace));
        _gallery.Picker.Typed = "Shirley";
        Task saving = _gallery.Picker.AddCommand.ExecuteAsync(null);

        await Started(_repository.Entered);

        // The user does not wait for the save. They click the other face.
        _gallery.BeginNamingFaceCommand.Execute(Face(SecondFace));

        _repository.Release();
        await saving;

        Assert.True(_gallery.Picker.IsOpen, "The list closed itself while it was being answered.");
        Assert.Equal(SecondFace, _gallery.FacingBeingNamed?.FaceId);
    }

    /// <summary>
    /// And the answer to it actually reaches the library.
    /// </summary>
    /// <remarks>
    /// The half that matters. The question being on screen is worth nothing if
    /// choosing a name writes nothing, which is precisely what the user saw.
    /// </remarks>
    [Fact]
    public async Task TheSecondNameIsWrittenRatherThanDropped()
    {
        await ShowTheFacesAsync();

        _gallery.BeginNamingFaceCommand.Execute(Face(FirstFace));
        _gallery.Picker.Typed = "Shirley";
        Task saving = _gallery.Picker.AddCommand.ExecuteAsync(null);

        await Started(_repository.Entered);
        _gallery.BeginNamingFaceCommand.Execute(Face(SecondFace));
        _repository.Release();
        await saving;

        _gallery.Picker.Typed = "Ta Tong";
        await _gallery.Picker.AddCommand.ExecuteAsync(null);

        Assert.Contains(
            _repository.Assigned,
            told => told.FaceId == SecondFace && told.Name == "Ta Tong");
    }

    /// <summary>
    /// Nobody being asked about is still cleared, which is what the re-read is for.
    /// </summary>
    /// <remarks>
    /// The fix keeps the question only while one is on screen. Kept
    /// unconditionally it would leave a stale face behind after every
    /// confirmation, and the next name typed would land on the previous face.
    /// </remarks>
    [Fact]
    public async Task WithNoQuestionOnScreenTheReReadClearsIt()
    {
        await ShowTheFacesAsync();

        _gallery.BeginNamingFaceCommand.Execute(Face(FirstFace));
        _gallery.Picker.Typed = "Shirley";
        Task saving = _gallery.Picker.AddCommand.ExecuteAsync(null);

        await Started(_repository.Entered);
        _repository.Release();
        await saving;

        Assert.False(_gallery.Picker.IsOpen);
        Assert.Null(_gallery.FacingBeingNamed);
    }

    /// <summary>
    /// Stepping on to the next picture while a name is still saving leaves the
    /// next picture's boxes alone.
    /// </summary>
    /// <remarks>
    /// Caught on a screen recording: name a face, press the arrow, and the boxes
    /// on the picture you land on are drawn and then vanish half a second later.
    /// The save, finishing, re-read the faces of the picture it was made on -
    /// and that re-read cleared the boxes on screen before checking which
    /// picture was open, then found it was not its own and stopped there.
    /// </remarks>
    [Fact]
    public async Task SteppingOnWhileANameSavesKeepsTheNextPicturesBoxes()
    {
        await ShowTheFacesAsync();

        _gallery.BeginNamingFaceCommand.Execute(Face(FirstFace));
        _gallery.Picker.Typed = "Shirley";
        Task saving = _gallery.Picker.AddCommand.ExecuteAsync(null);
        await Started(_repository.Entered);

        // The arrow: the next picture opens and draws its own two faces.
        _gallery.OpenFrom(new PlainGrid(new TileWindow(_thumbnails)), Tile(NextPhotograph));
        await TestWait.ForAsync(
            () => _gallery.OpenFaces.Count == 2, "the next picture's faces to be drawn");

        _repository.Release();
        await saving;

        Assert.Equal(NextPhotograph, _gallery.OpenTile?.Item.Id);
        Assert.Equal(2, _gallery.OpenFaces.Count);
    }

    /// <summary>
    /// Naming a face saves the name and asks for a round, and runs no round of
    /// its own.
    /// </summary>
    /// <remarks>
    /// The round weighs every face in the library against everybody. Run inside
    /// the save, naming ten faces in a row queued ten of them.
    /// </remarks>
    [Fact]
    public async Task NamingAFace_AsksForARoundRatherThanRunningOne()
    {
        int asked = 0;
        _gallery.SuggestionsOutdated += (_, _) => asked++;
        await ShowTheFacesAsync();

        _gallery.BeginNamingFaceCommand.Execute(Face(FirstFace));
        _gallery.Picker.Typed = "Shirley";
        Task saving = _gallery.Picker.AddCommand.ExecuteAsync(null);
        await Started(_repository.Entered);
        _repository.Release();
        await saving;

        Assert.Equal(1, asked);
        Assert.Equal(0, _repository.Rounds);
        Assert.Contains(_repository.Assigned, told => told.FaceId == FirstFace);
    }

    /// <summary>
    /// Names given faster than they save ask for one round between them, once
    /// the last of them is saved.
    /// </summary>
    /// <remarks>
    /// Each save is a few hundred milliseconds, so asking after every one spread
    /// ten quick names over longer than a round takes and the runner never saw
    /// them together: measured on a copy of the real library, ten names ran
    /// eight rounds that way, and one when the last save asks.
    /// </remarks>
    [Fact]
    public async Task ASaveThatFailsUnexpectedly_DoesNotStopLaterNamesAskingForARound()
    {
        int asked = 0;
        _gallery.SuggestionsOutdated += (_, _) => asked++;
        await ShowTheFacesAsync();
        _repository.Release();
        _repository.FailNext(new InvalidCastException("not a failure the screen expects"));

        _gallery.BeginNamingFaceCommand.Execute(Face(FirstFace));
        _gallery.Picker.Typed = "Shirley";
        await Assert.ThrowsAsync<InvalidCastException>(
            () => _gallery.Picker.AddCommand.ExecuteAsync(null));

        _gallery.BeginNamingFaceCommand.Execute(Face(SecondFace));
        _gallery.Picker.Typed = "Ta Tong";
        await _gallery.Picker.AddCommand.ExecuteAsync(null);

        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task NamesQueuedBehindEachOther_AskForOneRound()
    {
        int asked = 0;
        _gallery.SuggestionsOutdated += (_, _) => asked++;
        await ShowTheFacesAsync();

        _gallery.BeginNamingFaceCommand.Execute(Face(FirstFace));
        _gallery.Picker.Typed = "Shirley";
        Task first = _gallery.Picker.AddCommand.ExecuteAsync(null);
        await Started(_repository.Entered);

        // The second name is given while the first is still being written, so it
        // waits its turn behind it.
        _gallery.BeginNamingFaceCommand.Execute(Face(SecondFace));
        _gallery.Picker.Typed = "Ta Tong";
        Task second = _gallery.Picker.AddCommand.ExecuteAsync(null);

        Assert.Equal(0, asked);

        _repository.Release();
        await Task.WhenAll(first, second);

        Assert.Equal(1, asked);
        Assert.Contains(_repository.Assigned, told => told.FaceId == SecondFace);
    }

    /// <summary>
    /// In the app, the name is saved and the viewer handed back while the round
    /// it asked for is still running - and the corner bar says so.
    /// </summary>
    [Fact]
    public async Task NamingAFace_ReturnsBeforeTheRoundItAskedForHasFinished()
    {
        MainViewModel shell = TheShell();
        await ShowTheFacesAsync();

        _gallery.BeginNamingFaceCommand.Execute(Face(FirstFace));
        _gallery.Picker.Typed = "Shirley";
        Task saving = _gallery.Picker.AddCommand.ExecuteAsync(null);
        await Started(_repository.Entered);
        _repository.Release();
        await saving;

        // The round is held at its write by the test, so it cannot have finished.
        await Started(_repository.Proposing);
        Assert.True(shell.Suggestions.IsVisible, "nothing said a round was running");
        Assert.Equal(SuggestionRounds.RunningCaption, shell.Suggestions.Caption);
        Assert.Equal(2, _gallery.OpenFaces.Count);

        // The corner it is drawn in starts above the viewer's own buttons -
        // Close, delete and the turns sit along the bottom right, reaching 32
        // above the content row.
        Assert.True(shell.NoticeMargin.Bottom > 32, "the bar would sit on the viewer's Close button");

        _repository.FinishRound();
        await shell.Suggestions.Settled;
        await shell.SuggestionsShown;

        Assert.False(shell.Suggestions.IsVisible);
        Assert.Equal(1, _repository.Rounds);
    }

    /// <summary>The shell around this viewer, which runs the rounds it asks for.</summary>
    private MainViewModel TheShell()
    {
        IServiceScopeFactory scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
        var workingFolder = new WorkingFolder(_root);

        return new MainViewModel(
            scopeFactory,
            _gallery,
            new PeopleViewModel(scopeFactory, _thumbnails),
            new DuplicatesViewModel(scopeFactory, _thumbnails),
            _thumbnails,
            new FileActivityLog(workingFolder));
    }

    /// <summary>Opens the picture with its faces drawn, as the viewer would.</summary>
    private async Task ShowTheFacesAsync()
    {
        var grid = new TileWindow(_thumbnails);

        _gallery.ShowFaceNames = true;
        _gallery.OpenFrom(new PlainGrid(grid), Tile());

        await TestWait.ForAsync(() => _gallery.OpenFaces.Count == 2, "the faces to be drawn");
    }

    private PhotoFaceItem Face(int faceId) =>
        _gallery.OpenFaces.Single(face => face.FaceId == faceId);

    private static GalleryTile Tile(int assetId = ThePhotograph) =>
        new(new GalleryItem(
            assetId,
            @"album\photo.jpg",
            "photo.jpg",
            "album",
            @"C:\pictures\album\photo.jpg",
            "thumb.jpg",
            new DateTime(2014, 8, 3, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2014, 8, 3, 0, 0, 0, DateTimeKind.Unspecified),
            0,
            AssetKind.Photo));

    /// <summary>
    /// Waits for the write to be under way, and fails rather than hanging.
    /// </summary>
    /// <remarks>
    /// The screen swallows a save it cannot start - a missing service reads as
    /// the same failure as an unwritable index - so without this the whole test
    /// run would sit here for ever waiting for a write that was never attempted.
    /// </remarks>
    private static async Task Started(Task entered)
    {
        Task first = await Task.WhenAny(entered, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.True(
            ReferenceEquals(first, entered),
            "The confirmation never reached the library; it failed before the write.");
    }

    public void Dispose()
    {
        _repository.Release();
        _repository.FinishRound();
        _services.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not go is not a failed test.
        }
    }

    /// <summary>Two faces on the one photograph, neither of them named yet.</summary>
    private sealed class TwoFacesOnOnePhoto : IPeopleReader
    {
        public Task<IReadOnlyList<FaceOnPhoto>> GetFacesOnAsync(
            int assetId, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceOnPhoto>>(
                [Unnamed(FirstFace, 10), Unnamed(SecondFace, 200)]);

        public Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<PersonDirectoryEntry>>([]);

        private static FaceOnPhoto Unnamed(int faceId, int left) => new(
            faceId,
            new FaceBounds(left, 10, 60, 60),
            DetectScore: 0.99f,
            PersonId: null,
            PersonName: null,
            Source: AssignmentSource.Proposed,
            IsIgnored: false);

        public Task<IReadOnlyList<FaceRecord>> GetFacesAsync(
            bool includeEmbeddings, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceRecord>>([]);

        public Task<IReadOnlyList<FaceRecord>> GetFacesOnPicturesOfAsync(
            IReadOnlyCollection<int> faceIds, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceRecord>>([]);

        public Task<IReadOnlyList<FaceSample>> GetSamplesAsync(
            int personId, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceSample>>([]);

        public Task<IReadOnlyList<Person>> GetPeopleAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<Person>>([]);

        public Task<IReadOnlyList<FaceRejection>> GetRejectionsAsync(
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceRejection>>([]);

        public Task<IReadOnlyList<FaceConfirmation>> GetConfirmationsAsync(
            IReadOnlyCollection<int> faceIds, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceConfirmation>>([]);
    }

    /// <summary>
    /// A library whose write does not return until the test says so.
    /// </summary>
    /// <remarks>
    /// This is what turns a race into a test. The screen stays live inside the
    /// wait, which is exactly the window the user clicks the second face in.
    /// </remarks>
    private sealed class BlockingPeople : IPeopleRepository
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Dictionary<int, string> _names = [];

        /// <summary>Completes once a write is under way.</summary>
        public Task Entered => _entered.Task;

        public List<(int FaceId, string Name)> Assigned { get; } = [];

        public void Release() => _released.TrySetResult();

        private Exception? _failNext;

        /// <summary>Makes the next write throw this, once.</summary>
        public void FailNext(Exception failure) => _failNext = failure;

        public async Task AssignAsync(
            int personId,
            IReadOnlyList<ScoredFace> faces,
            AssignmentSource source,
            CancellationToken token = default)
        {
            _entered.TrySetResult();
            await _released.Task.ConfigureAwait(false);

            if (Interlocked.Exchange(ref _failNext, null) is Exception failure)
            {
                throw failure;
            }

            if (source != AssignmentSource.Confirmed)
            {
                return;
            }

            string name = _names.TryGetValue(personId, out string? known) ? known : "?";
            lock (Assigned)
            {
                Assigned.AddRange(faces.Select(face => (face.FaceId, name)));
            }
        }

        public Task<int> EnsurePersonAsync(string displayName, CancellationToken token = default)
        {
            int id = _names.FirstOrDefault(pair => pair.Value == displayName).Key;
            if (id == 0)
            {
                id = _names.Count + 1;
                _names[id] = displayName;
            }

            return Task.FromResult(id);
        }

        /// <summary>Completes once a suggestion round has reached its write.</summary>
        public Task Proposing => _proposing.Task;

        /// <summary>Lets a suggestion round held at its write finish.</summary>
        public void FinishRound() => _roundReleased.TrySetResult();

        /// <summary>How many suggestion rounds reached their write.</summary>
        public int Rounds { get; private set; }

        private readonly TaskCompletionSource _proposing =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _roundReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Held until the test lets it go, so a test can tell the name being
        /// saved apart from the round that follows it.
        /// </summary>
        public async Task ReplaceProposalsAsync(
            IReadOnlyDictionary<int, IReadOnlyList<ScoredFace>> proposals,
            CancellationToken token = default)
        {
            Rounds++;
            _proposing.TrySetResult();
            await _roundReleased.Task.ConfigureAwait(false);
        }

        public Task ReplaceErasAsync(
            int personId, IReadOnlyList<PersonEra> eras, CancellationToken token = default) =>
            Task.CompletedTask;

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
}

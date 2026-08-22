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
/// Answering "is this them?" from an eighty-pixel crop is guesswork when the
/// crop is half hair and half shoulder. These cover walking the same queue on
/// the whole pictures instead.
/// </summary>
/// <remarks>
/// No renditions are written, so nothing decodes and the picture stays null.
/// That is deliberate: what is under test is which face is being asked about and
/// what the answer does to it, neither of which should depend on a file being
/// readable.
/// </remarks>
public sealed class FaceInspectionTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly RecordsWhatItIsTold _repository = new();
    private readonly PeopleViewModel _people;

    public FaceInspectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-inspect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _services = new ServiceCollection()
            .AddSingleton<IPeopleReader, NobodyIsKnownYet>()
            .AddSingleton<IPeopleRepository>(_repository)
            .AddSingleton<IGalleryReader, NoPicturesToShow>()
            .AddTransient<QueryGalleryHandler>()
            .AddTransient<GetPersonReviewHandler>()
            .AddTransient<GetPeopleBoardHandler>()
            .AddTransient<AssignFacesHandler>()
            .BuildServiceProvider();

        _people = new PeopleViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));

        _people.SelectedPerson = Person(1, "Ana Lim");
        Seed();
    }

    /// <summary>The queue the screen would have built for the chosen person.</summary>
    private void Seed()
    {
        // Everyone named, which is what the "no, it is..." list offers.
        _people.Named.Clear();
        _people.Named.Add(Person(1, "Ana Lim"));
        _people.Named.Add(Person(2, "Ana Reyes"));

        _people.Proposals.Clear();
        for (int i = 0; i < 3; i++)
        {
            _people.Proposals.Add(new FaceCropItem(new FaceThumbnail(
                i + 1, i + 1, $"face-{i}.jpg", new FaceBounds(10, 10, 40, 40),
                new DateTime(2018, 5, 4, 0, 0, 0, DateTimeKind.Utc),
                @"album\photo.jpg", @"C:\pictures\album\photo.jpg")));
        }
    }

    [Fact]
    public async Task Inspect_WithNoFaceNamedOpensTheTopOfTheQueue()
    {
        // What the "Check one at a time" button does: there is no face to point
        // at, only a queue to start.
        await _people.InspectFaceCommand.ExecuteAsync(null);

        Assert.True(_people.IsInspecting);
        Assert.Same(_people.Proposals[0], _people.Inspected);
        Assert.Equal("1 of 3 left", _people.InspectedPosition);
    }

    [Fact]
    public async Task Inspect_OpensTheFaceThatWasAskedFor()
    {
        await _people.InspectFaceCommand.ExecuteAsync(_people.Proposals[2]);

        Assert.Same(_people.Proposals[2], _people.Inspected);
        Assert.Equal("3 of 3 left", _people.InspectedPosition);
    }

    [Fact]
    public async Task Inspect_WithNothingToReviewOpensNothing()
    {
        _people.Proposals.Clear();

        await _people.InspectFaceCommand.ExecuteAsync(null);

        Assert.False(_people.IsInspecting);
    }

    [Fact]
    public async Task Step_MovesThroughTheQueueAndStopsAtEitherEnd()
    {
        await _people.InspectFaceCommand.ExecuteAsync(null);

        await _people.InspectPreviousCommand.ExecuteAsync(null);
        Assert.Same(_people.Proposals[0], _people.Inspected);

        await _people.InspectNextCommand.ExecuteAsync(null);
        await _people.InspectNextCommand.ExecuteAsync(null);
        Assert.Same(_people.Proposals[2], _people.Inspected);

        // Not back to the top. Wrapping would present a face that has already
        // been dealt with as though it were new work.
        await _people.InspectNextCommand.ExecuteAsync(null);
        Assert.Same(_people.Proposals[2], _people.Inspected);
    }

    [Fact]
    public async Task Close_ComesBackToTheQuestionsWithTheRestOfTheQueueOnThem()
    {
        // Closing brings the board up to date, because answering changes the
        // counts on it. That rebuild used to empty the queue as well, so Close
        // put the user back on the person with nothing left to answer - the
        // faces they had not reached yet had gone with the ones they had.
        _people.IsConfirming = true;
        await _people.InspectFaceCommand.ExecuteAsync(null);
        await _people.KeepInspectedCommand.ExecuteAsync(null);

        await _people.CloseInspectCommand.ExecuteAsync(null);

        Assert.False(_people.IsInspecting);
        Assert.True(_people.ShowingConfirmQueue, "Close belongs to the queue it was opened from");
        Assert.Equal(2, _people.Proposals.Count);
    }

    [Fact]
    public async Task Close_AfterTheLastAnswerHandsBackToTheirPictures()
    {
        _people.IsConfirming = true;
        await _people.InspectFaceCommand.ExecuteAsync(null);

        // Answering the last one closes the picture on its own.
        await _people.KeepInspectedCommand.ExecuteAsync(null);
        await _people.KeepInspectedCommand.ExecuteAsync(null);
        await _people.KeepInspectedCommand.ExecuteAsync(null);

        Assert.False(_people.IsInspecting);
        Assert.Empty(_people.Proposals);

        // This used to assert that the queue was still on screen, which is what
        // the user was actually left looking at: a heading over a blank panel,
        // with no answer buttons under it because they hide themselves when
        // there is nothing to answer, and the pictures the work had just changed
        // one click away behind a link.
        Assert.False(_people.ShowingConfirmQueue);
        Assert.True(_people.ShowingPhotos);
    }

    [Fact]
    public void ChoosingSomebodyElse_StillClearsTheQuestionsAskedAboutTheFirst()
    {
        _people.IsConfirming = true;
        Assert.NotEmpty(_people.Proposals);

        _people.SelectedPerson = Person(2, "Ana Reyes");

        Assert.Empty(_people.Proposals);
        Assert.False(_people.IsConfirming, "a name opens on their pictures, not on a queue of work");
    }

    [Fact]
    public async Task Keep_SavesTheFaceAsThemAndTakesItOutOfTheQueue()
    {
        // The whole point of answering here rather than ticking a box: an
        // answered face is dealt with, so it should not be waiting on the screen
        // you go back to.
        await _people.InspectFaceCommand.ExecuteAsync(null);

        await _people.KeepInspectedCommand.ExecuteAsync(null);

        Assert.Equal((1, 1, AssignmentSource.Confirmed), Assert.Single(_repository.Assignments));
        Assert.Equal(2, _people.Proposals.Count);
        Assert.DoesNotContain(_people.Proposals, crop => crop.FaceId == 1);
    }

    [Fact]
    public async Task LeaveOut_RecordsARefusalSoItIsNeverOfferedAgain()
    {
        await _people.InspectFaceCommand.ExecuteAsync(null);

        await _people.DropInspectedCommand.ExecuteAsync(null);

        Assert.Equal((1, 1, AssignmentSource.Rejected), Assert.Single(_repository.Assignments));
        Assert.Equal(2, _people.Proposals.Count);
    }

    [Fact]
    public async Task Answering_MovesOnToWhateverTookItsPlace()
    {
        await _people.InspectFaceCommand.ExecuteAsync(null);
        FaceCropItem second = _people.Proposals[1];

        await _people.KeepInspectedCommand.ExecuteAsync(null);

        Assert.Same(second, _people.Inspected);
        Assert.Equal("1 of 2 left", _people.InspectedPosition);
    }

    [Fact]
    public async Task Answering_TheLastOneStepsBackRatherThanOffTheEnd()
    {
        await _people.InspectFaceCommand.ExecuteAsync(_people.Proposals[2]);
        FaceCropItem second = _people.Proposals[1];

        await _people.KeepInspectedCommand.ExecuteAsync(null);

        Assert.Same(second, _people.Inspected);
    }

    [Fact]
    public async Task Answering_TheOnlyOneLeftClosesThePicture()
    {
        // An empty queue behind an open picture is a dead end: there is nothing
        // to answer and nothing on screen saying so.
        _people.Proposals.Clear();
        _people.Proposals.Add(new FaceCropItem(new FaceThumbnail(
            9, 9, "face.jpg", new FaceBounds(0, 0, 40, 40),
            new DateTime(2018, 5, 4, 0, 0, 0, DateTimeKind.Utc),
            @"album\photo.jpg", @"C:\pictures\album\photo.jpg")));

        await _people.InspectFaceCommand.ExecuteAsync(null);
        await _people.KeepInspectedCommand.ExecuteAsync(null);

        Assert.False(_people.IsInspecting);
    }

    [Fact]
    public async Task Answering_KeepsTheFaceWhenTheAnswerCouldNotBeSaved()
    {
        // An answer that was not written is an answer still to give, so it stays
        // in the queue rather than vanishing as though it had been dealt with.
        await _people.InspectFaceCommand.ExecuteAsync(null);
        _repository.Fails = true;

        await _people.KeepInspectedCommand.ExecuteAsync(null);

        Assert.Equal(3, _people.Proposals.Count);
        Assert.Same(_people.Proposals[0], _people.Inspected);
        Assert.Contains("could not be saved", _people.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answered_IsTalliedWhileTheQueueIsWorked()
    {
        await _people.InspectFaceCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, _people.AnsweredCaption);

        await _people.KeepInspectedCommand.ExecuteAsync(null);
        Assert.Equal("1 kept so far", _people.AnsweredCaption);

        await _people.DropInspectedCommand.ExecuteAsync(null);
        Assert.Equal("1 kept and 1 left out so far", _people.AnsweredCaption);
    }

    [Fact]
    public async Task Close_PutsThePictureDown()
    {
        await _people.InspectFaceCommand.ExecuteAsync(null);

        await _people.CloseInspectCommand.ExecuteAsync(null);

        Assert.False(_people.IsInspecting);
        Assert.Null(_people.Inspected);
        Assert.Null(_people.InspectedPicture);
    }

    [Fact]
    public async Task Close_SaysWhatWasAnsweredWithoutGoingLookingForMore()
    {
        // Looking through the library again on the way out refilled the queue to
        // its limit, so answering a dozen faces and closing gave back a list the
        // same length as before and read as nothing having happened. Whether to
        // go looking is a decision, and it has its own button.
        await _people.InspectFaceCommand.ExecuteAsync(null);
        await _people.KeepInspectedCommand.ExecuteAsync(null);

        await _people.CloseInspectCommand.ExecuteAsync(null);

        Assert.Equal(0, _repository.ErasReplaced);
        Assert.Equal(0, _repository.ProposalsCleared);
        Assert.Contains("1 kept", _people.Status, StringComparison.Ordinal);
        Assert.Contains("Check everyone", _people.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Close_WithNothingAnsweredSaysNothing()
    {
        await _people.InspectFaceCommand.ExecuteAsync(null);

        await _people.CloseInspectCommand.ExecuteAsync(null);

        Assert.Equal(0, _repository.ErasReplaced);
        Assert.Equal(string.Empty, _people.Status);
    }

    [Fact]
    public async Task ChoosingAnotherPerson_ClosesWhateverWasOpen()
    {
        // The queue on screen belongs to one person. Leaving a picture from the
        // last person's queue open over the next person's is worse than useless.
        await _people.InspectFaceCommand.ExecuteAsync(null);
        Assert.True(_people.IsInspecting);

        _people.SelectedPerson = Person(2, "Ana Reyes");

        Assert.False(_people.IsInspecting);
    }

    [Fact]
    public async Task GoingBackToTheList_ClosesWhateverWasOpen()
    {
        await _people.InspectFaceCommand.ExecuteAsync(null);
        Assert.True(_people.IsInspecting);

        _people.ClearSelectionCommand.Execute(null);

        Assert.False(_people.IsInspecting);
    }

    [Fact]
    public async Task Reassign_OffersEveryNameAndMarksWhoseQueueThisIs()
    {
        // The person being reviewed comes marked because that is the answer
        // being changed - and picking them again is a way of saying yes, which
        // the user should be able to see is the same thing.
        await _people.InspectFaceCommand.ExecuteAsync(null);

        _people.ReassignInspectedCommand.Execute(null);

        Assert.True(_people.Reassign.IsOpen);
        Assert.Equal(["Ana Lim", "Ana Reyes"], _people.Reassign.Choices.Select(c => c.DisplayName));
        Assert.True(_people.Reassign.Choices.Single(c => c.DisplayName == "Ana Lim").IsCurrent);
    }

    [Fact]
    public async Task Reassign_RecordsTheFaceAsSomebodyElseAndMovesOn()
    {
        // The answer this screen was missing. "No" throws away what the user
        // knows; naming the right person keeps it, and it is an example for
        // somebody rather than a refusal for nobody.
        await _people.InspectFaceCommand.ExecuteAsync(null);
        FaceCropItem second = _people.Proposals[1];

        _people.ReassignInspectedCommand.Execute(null);
        await _people.Reassign.ChooseCommand.ExecuteAsync(_people.Reassign.Choices[1]);

        Assert.Equal(["Ana Reyes"], _repository.PeopleEnsured);
        Assert.Equal((2, 1, AssignmentSource.Confirmed), Assert.Single(_repository.Assignments));
        Assert.DoesNotContain(_people.Proposals, crop => crop.FaceId == 1);
        Assert.Same(second, _people.Inspected);
        Assert.False(_people.Reassign.IsOpen);
    }

    [Fact]
    public async Task Reassign_ToSomebodyNotOnTheListAtAllMakesThem()
    {
        await _people.InspectFaceCommand.ExecuteAsync(null);

        _people.ReassignInspectedCommand.Execute(null);
        _people.Reassign.Typed = "Grandma";
        await _people.Reassign.AddCommand.ExecuteAsync(null);

        Assert.Equal(["Grandma"], _repository.PeopleEnsured);
        Assert.Equal(AssignmentSource.Confirmed, Assert.Single(_repository.Assignments).Source);
    }

    [Fact]
    public async Task Reassign_IsCountedApartFromKeepingAndLeavingOut()
    {
        await _people.InspectFaceCommand.ExecuteAsync(null);

        await _people.KeepInspectedCommand.ExecuteAsync(null);
        _people.ReassignInspectedCommand.Execute(null);
        await _people.Reassign.ChooseCommand.ExecuteAsync(_people.Reassign.Choices[1]);

        Assert.Equal("1 kept and 1 given to someone else so far", _people.AnsweredCaption);
    }

    [Fact]
    public async Task Reassign_KeepsTheFaceWhenTheAnswerCouldNotBeSaved()
    {
        await _people.InspectFaceCommand.ExecuteAsync(null);
        _people.ReassignInspectedCommand.Execute(null);
        _repository.Fails = true;

        await _people.Reassign.ChooseCommand.ExecuteAsync(_people.Reassign.Choices[1]);

        Assert.Equal(3, _people.Proposals.Count);
        Assert.Contains("could not be saved", _people.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Close_PutsTheNameListDownBeforeThePicture()
    {
        // Escape and Close mean "put down the thing on top". Closing the whole
        // screen from underneath an open question is not what was asked for.
        await _people.InspectFaceCommand.ExecuteAsync(null);
        _people.ReassignInspectedCommand.Execute(null);

        await _people.CloseInspectCommand.ExecuteAsync(null);
        Assert.False(_people.Reassign.IsOpen);
        Assert.True(_people.IsInspecting);

        await _people.CloseInspectCommand.ExecuteAsync(null);
        Assert.False(_people.IsInspecting);
    }

    [Fact]
    public async Task NamingSomebodyNew_TellsTheShellTheLibraryChanged()
    {
        // The status bar counts people and photos, and it used to hear only from
        // the long passes - so naming somebody, or deleting a photograph, left
        // the number sitting there unchanged and reading as a failure.
        bool told = false;
        _people.LibraryChanged += (_, _) => told = true;

        _people.NewPersonName = "Grandma";
        await _people.AddPersonCommand.ExecuteAsync(null);

        Assert.True(told, "the shell was never told to count again");
    }

    [Fact]
    public async Task NamingNobody_SaysNothingToTheShell()
    {
        // An empty box is a mistake, not a change, and re-reading the counts for
        // it would be work done for nothing.
        bool told = false;
        _people.LibraryChanged += (_, _) => told = true;

        _people.NewPersonName = "   ";
        await _people.AddPersonCommand.ExecuteAsync(null);

        Assert.False(told);
    }

    [Fact]
    public async Task Reassign_TheNameListGoesDownWhenTheFaceUnderItChanges()
    {
        // The list writes onto whichever face is open at the moment a name is
        // clicked, not the one it was opened about. Left up over the next face
        // it put the answer on the wrong picture and left the face it had asked
        // about sitting in the queue unanswered - and the arrow buttons under it
        // stayed live the whole time, unlike the arrow keys.
        await _people.InspectFaceCommand.ExecuteAsync(null);
        _people.ReassignInspectedCommand.Execute(null);
        Assert.True(_people.Reassign.IsOpen);

        await _people.InspectNextCommand.ExecuteAsync(null);

        Assert.False(_people.Reassign.IsOpen);
    }

    [Fact]
    public async Task Reassign_AnsweringTheLastFaceWithTheListOpenStillPutsThePictureDown()
    {
        // Close puts the list down before the picture, which is right. But when
        // the answer itself empties the queue, the close that follows was
        // swallowed by the list instead, and the picture stayed up over nothing
        // reading "0 of 0 left" until Close was pressed a second time.
        _people.IsConfirming = true;
        await _people.InspectFaceCommand.ExecuteAsync(null);
        await _people.KeepInspectedCommand.ExecuteAsync(null);
        await _people.KeepInspectedCommand.ExecuteAsync(null);
        Assert.Single(_people.Proposals);

        _people.ReassignInspectedCommand.Execute(null);
        await _people.Reassign.ChooseCommand.ExecuteAsync(_people.Reassign.Choices[1]);

        Assert.False(_people.IsInspecting);
        Assert.Empty(_people.Proposals);
    }

    [Fact]
    public async Task Reassign_ToThePersonWhoseQueueThisIsCountsAsKeeping()
    {
        // Their name is marked in the list precisely so that it can be picked,
        // and picking it means yes. Counting it as a face given away then
        // reported back the opposite of what the user had just said.
        await _people.InspectFaceCommand.ExecuteAsync(null);

        _people.ReassignInspectedCommand.Execute(null);
        await _people.Reassign.ChooseCommand.ExecuteAsync(_people.Reassign.Choices[0]);

        Assert.Equal("1 kept so far", _people.AnsweredCaption);
    }

    [Fact]
    public async Task Deleting_TheLastQuestionsPictureBringsTheScreenUpToDate()
    {
        // Deleting is not an answer about anybody, so it leaves the tally at
        // zero - and the tally was the only thing that decided whether the
        // screen was rebuilt on the way out. So the button offering the queue
        // kept a count whose pictures no longer existed, and pressing it opened
        // an empty queue with nothing under it but the way back.
        _people.IsConfirming = true;
        await _people.InspectFaceCommand.ExecuteAsync(null);

        await _people.AfterInspectedDeletedAsync(Deleted());
        await _people.AfterInspectedDeletedAsync(Deleted());
        await _people.AfterInspectedDeletedAsync(Deleted());

        Assert.False(_people.IsInspecting);
        Assert.Empty(_people.Proposals);
        Assert.False(_people.ShowingConfirmQueue);

        // Still no tally, because nobody was answered.
        Assert.Equal(string.Empty, _people.Status);
    }

    /// <summary>One photograph gone, which is what the shell reports back.</summary>
    private static PhotoRemovalResult Deleted() => new(1, [], false, [], []);

    private static PersonItem Person(int id, string name) =>
        new(new PersonSummary(id, name, 0, 0, 0, null));

    /// <summary>
    /// Enough of the read side for the screen to change person, and nothing
    /// more: what is under test is what the screen does when it does.
    /// </summary>
    /// <summary>
    /// Two names, each with a face already confirmed to them.
    /// </summary>
    /// <remarks>
    /// The confirmed faces matter: <c>GetPeopleBoardHandler</c> returns an empty
    /// board when the library holds no faces at all, and an empty board takes
    /// the selected person with it. Without them, rebuilding the board on the
    /// way out of the picture would lose the person for a reason that has
    /// nothing to do with what is under test.
    /// </remarks>
    private sealed class NobodyIsKnownYet : IPeopleReader
    {
        public Task<IReadOnlyList<FaceRecord>> GetFacesAsync(
            bool includeEmbeddings, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceRecord>>(
            [
                Confirmed(101, personId: 1),
                Confirmed(102, personId: 2),
            ]);

        private static FaceRecord Confirmed(int faceId, int personId) => new(
            faceId,
            faceId,
            $"settled-{faceId}.jpg",
            new FaceBounds(10, 10, 40, 40),
            0.99f,
            new DateTime(2018, 5, 4, 0, 0, 0, DateTimeKind.Utc),
            @"album\settled.jpg",
            @"C:\pictures\album\settled.jpg",
            new FaceEmbedding(new float[512]),
            personId,
            AssignmentSource.Confirmed,
            IsIgnored: false);

        public Task<IReadOnlyList<FaceSample>> GetSamplesAsync(
            int personId, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceSample>>([]);

        public Task<IReadOnlyList<FaceOnPhoto>> GetFacesOnAsync(
            int assetId, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceOnPhoto>>([]);

        public Task<IReadOnlyList<Person>> GetPeopleAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<Person>>(
            [
                new Person { Id = 1, DisplayName = "Ana Lim" },
                new Person { Id = 2, DisplayName = "Ana Reyes" },
            ]);

        public Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<PersonDirectoryEntry>>([]);

        public Task<IReadOnlyList<FaceRejection>> GetRejectionsAsync(
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceRejection>>([]);
    }

    /// <summary>
    /// A library with no pictures in it, for the one call these tests reach.
    /// </summary>
    /// <remarks>
    /// Opening a person loads their photographs, which happens on the way out of
    /// the picture as well as on the way in. Nothing here looks at that grid, so
    /// it answers with an empty page; every other question belongs to a pass and
    /// would be a surprise worth failing on.
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

    /// <summary>The write side, remembering what it was asked to do.</summary>
    private sealed class RecordsWhatItIsTold : IPeopleRepository
    {
        public List<(int PersonId, int FaceId, AssignmentSource Source)> Assignments { get; } = [];

        /// <summary>How many times the expensive follow-up work was done.</summary>
        public int ErasReplaced { get; private set; }

        /// <summary>
        /// How many times the outstanding questions were withdrawn, which is
        /// what re-proposing does before it asks them again.
        /// </summary>
        public int ProposalsCleared { get; private set; }

        public bool Fails { get; set; }

        public Task AssignAsync(
            int personId,
            IReadOnlyList<ScoredFace> faces,
            AssignmentSource source,
            CancellationToken token = default)
        {
            if (Fails)
            {
                throw new IOException("the index is not writable");
            }

            Assignments.AddRange(faces.Select(face => (personId, face.FaceId, source)));
            return Task.CompletedTask;
        }

        public Task ReplaceErasAsync(
            int personId, IReadOnlyList<PersonEra> eras, CancellationToken token = default)
        {
            ErasReplaced++;
            return Task.CompletedTask;
        }

        /// <summary>Names this was asked to find or create, in order.</summary>
        public List<string> PeopleEnsured { get; } = [];

        private readonly Dictionary<string, int> _ids =
            new() { ["Ana Lim"] = 1, ["Ana Reyes"] = 2 };

        public Task<int> EnsurePersonAsync(string displayName, CancellationToken token = default)
        {
            if (Fails)
            {
                throw new IOException("the index is not writable");
            }

            PeopleEnsured.Add(displayName);

            if (!_ids.TryGetValue(displayName, out int id))
            {
                id = _ids.Count + 1;
                _ids[displayName] = id;
            }

            return Task.FromResult(id);
        }

        public Task RenamePersonAsync(
            int personId, string displayName, CancellationToken token = default) =>
            Task.CompletedTask;

        public int? BirthYearSet { get; private set; }

        public Task SetBirthYearAsync(
            int personId, int? birthYear, CancellationToken token = default)
        {
            BirthYearSet = birthYear;
            return Task.CompletedTask;
        }

        public Task ClearProposalsAsync(int personId, CancellationToken token = default)
        {
            ProposalsCleared++;
            return Task.CompletedTask;
        }

        public Task UnassignAsync(IReadOnlyList<int> faceIds, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task SetIgnoredAsync(
            IReadOnlyList<int> faceIds, bool ignored, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task RemovePersonAsync(int personId, CancellationToken token = default) =>
            Task.CompletedTask;
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

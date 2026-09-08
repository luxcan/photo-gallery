using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Albums;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
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
    private const int Shelved = 403;
    private const int Weekends = 501;

    private static readonly XNamespace s_wpf =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly FakeAlbums _repository = new();
    private readonly HeldCover _covers;
    private readonly AlbumsViewModel _albums;

    public EditAlbumTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-edit-album-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        string cover = Path.Combine(_root, "cover.jpg");
        File.WriteAllBytes(cover, Jpeg());
        _covers = new HeldCover(new FileSystemThumbnailStore(workingFolder), cover);

        _services = new ServiceCollection()
            .AddSingleton<IAlbumRepository>(_repository)
            .AddSingleton<ICollectionRepository, OneShelf>()
            .AddSingleton<IPeopleReader, NoPeople>()
            .AddSingleton<IPlaceReader, NoPlaces>()
            .BuildServiceProvider();

        _albums = new AlbumsViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(), _covers);
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

        Assert.Equal((Mine, "BBK Trip 2012"), _repository.Renamed.Single());
        Assert.Equal(Mine, _repository.RulesSet.Single().AlbumId);
        Assert.Equal(new DateOnly(2012, 3, 12), _repository.RulesSet.Single().Rule.From);
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

        Assert.Empty(_repository.Renamed);
        Assert.Single(_repository.RulesSet);
    }

    [Fact]
    public async Task SurroundingSpaceIsNotAChangeOfName()
    {
        await OpenForEditAsync(Mine);
        _albums.EditedName = "  BBK Trip  ";

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_repository.Renamed);
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
    /// A rule that cannot be read leaves no panel holding the last album's.
    /// </summary>
    /// <remarks>
    /// The panel used to open before the rule behind it was read, and a read
    /// that failed returned before the fields were touched. The panel then stood
    /// open on one album showing the album before it - and Save, which reads
    /// those same fields, wrote the first album's rule onto the second.
    /// </remarks>
    [Fact]
    public async Task AnAlbumWhoseRuleCannotBeRead_OpensNoPanelOnTheLastAlbumsRule()
    {
        _repository.Rule = new AlbumRule(
            new DateOnly(2019, 3, 20), new DateOnly(2019, 3, 20), [], []);

        await OpenForEditAsync(Mine);
        Assert.True(_albums.IsOneDay);

        _albums.CancelEditCommand.Execute(null);
        _repository.RuleReadFails = new IOException("the library is busy");
        await OpenForEditAsync(Suggested);

        // Shut, and said so where this album's name is. Open, it would have been
        // one album's name above another album's rule.
        Assert.False(_albums.IsEditing);
        Assert.Contains("could not be read", _albums.Status, StringComparison.Ordinal);

        // And the one thing that would have made it permanent is refused.
        Assert.False(_albums.SaveCommand.CanExecute(null));

        await _albums.SaveCommand.ExecuteAsync(null);
        Assert.Empty(_repository.RulesSet);
    }

    /// <summary>
    /// A read that lands after the user has moved on fills nothing in.
    /// </summary>
    /// <remarks>
    /// Two panels are the same panel, so a rule read for an album that was left
    /// behind arrives at the fields of whatever is open now. Whichever finished
    /// last used to win.
    /// </remarks>
    [Fact]
    public async Task ARuleThatArrivesLate_DoesNotLandInThePanelOpenedAfterIt()
    {
        _repository.Rule = new AlbumRule(
            new DateOnly(2019, 3, 20), new DateOnly(2019, 3, 20), [], []);
        _repository.Held = new TaskCompletionSource();

        await _albums.ReloadAsync();
        _albums.Selected = _albums.Showing.Single(item => item.Id == Mine);
        Task editing = _albums.EditCommand.ExecuteAsync(null);

        // Back out and start a new album instead, while that read is still out.
        _albums.CloseAlbumCommand.Execute(null);
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        Assert.True(_albums.IsNewAlbum);
        Assert.True(_albums.IsAnyDay);

        _repository.Held.SetResult();
        await editing;

        // The panel standing open is the new album's, and it still asks nothing
        // about the date. The rule that arrived after it belongs to an album
        // nobody is describing any more.
        Assert.True(_albums.IsNewAlbum);
        Assert.True(_albums.IsAnyDay);
        Assert.Null(_albums.RuleDay);
    }

    /// <summary>
    /// A cover arriving while the name is typed does not take it with it.
    /// </summary>
    /// <remarks>
    /// The wall decodes its covers in the background and swaps each row for a
    /// copy carrying the picture, which means re-pointing the open album at the
    /// copy. That is the wall keeping up with itself, not the user opening
    /// something else, so it must not do what opening something else does -
    /// which is fill the name box from the album, over the half-typed name the
    /// user is standing in. Save then compares the box with the stored name,
    /// finds them the same, sends no rename, and says "Saved."
    /// </remarks>
    [Fact]
    public async Task ACoverArrivingWhileTheNameIsTyped_DoesNotDiscardIt()
    {
        _repository.HasCover = true;
        await OpenForEditAsync(Mine);

        _albums.EditedName = "Harbour Weekend 2012";
        _covers.Release();
        await WaitFor(
            () => _albums.Wall[0].Cover is not null, "the cover to reach the wall");

        Assert.Equal("Harbour Weekend 2012", _albums.EditedName);

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Equal((Mine, "Harbour Weekend 2012"), _repository.Renamed.Single());
    }

    /// <summary>
    /// A cover arriving does not read the open album's photographs again.
    /// </summary>
    /// <remarks>
    /// Reading them again refills the grid, and refilling it clears its rows - a
    /// reset the list is bound to, which puts the reader back at the top of a
    /// wall of photographs they had scrolled down through. Nothing about the
    /// album changed; only its cover finished decoding.
    /// </remarks>
    [Fact]
    public async Task ACoverArriving_DoesNotReadTheOpenAlbumsPhotographsAgain()
    {
        _repository.HasCover = true;
        await OpenForEditAsync(Mine);

        int readBefore = _repository.MemberReads;
        _covers.Release();
        await WaitFor(
            () => _albums.Wall[0].Cover is not null, "the cover to reach the wall");

        Assert.Equal(readBefore, _repository.MemberReads);
    }

    /// <summary>
    /// Saving leaves the album open, including inside a collection.
    /// </summary>
    /// <remarks>
    /// A save that changed the name reads the library again, and reading it
    /// hands the band a new row for the shelf that is open. The wall used to
    /// take that announcement for the reader having gone somewhere and close the
    /// album under them - so what they got for pressing Save was the album they
    /// were editing thrown back to the shelf, with no message and nothing to
    /// press but the album again.
    /// </remarks>
    [Fact]
    public async Task SavingInsideACollection_LeavesTheAlbumOpen()
    {
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());
        _albums.Selected = _albums.Wall.Single();
        await _albums.EditCommand.ExecuteAsync(null);

        _albums.EditedName = "A weekend away";
        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Shelved, _albums.Selected?.Id);
        Assert.Equal(Weekends, _albums.Collections.Open?.Id);
    }

    /// <summary>
    /// Coming out of a collection still closes the album, which is the half the
    /// guard above it must not swallow.
    /// </summary>
    /// <remarks>
    /// What is behind the back chevron has moved, and a photograph grid left up
    /// over a wall it did not come from is how a back button starts lying.
    /// </remarks>
    [Fact]
    public async Task ComingOutOfACollection_StillClosesTheAlbum()
    {
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());
        _albums.Selected = _albums.Wall.Single();
        Assert.True(_albums.HasSelected);

        _albums.Collections.CloseCommand.Execute(null);

        Assert.False(_albums.HasSelected);
        Assert.True(_albums.ShowingTheStrip);
    }

    /// <summary>
    /// A library that will not answer is a sentence on the screen, not the end
    /// of the session.
    /// </summary>
    /// <remarks>
    /// The screen caught what a file read throws, and the library is a database
    /// too: SqliteException derives from DbException and DbUpdateException
    /// straight from Exception, so neither was matched. A locked database, or
    /// one on a drive that had gone away, went past the filter and into the
    /// handler in App.xaml.cs, which reports and then lets the app close.
    /// </remarks>
    [Fact]
    public async Task ALockedLibrary_IsSaidOnTheScreenRatherThanClosingTheApp()
    {
        _repository.ReadFails = new SqliteException("database is locked", 5);

        await _albums.ReloadAsync();

        Assert.Contains("could not be read", _albums.Status, StringComparison.Ordinal);
        Assert.Contains("database is locked", _albums.Status, StringComparison.Ordinal);
        Assert.False(_albums.IsBusy);
    }

    /// <summary>A write the database refuses leaves the panel open to try again.</summary>
    [Fact]
    public async Task AWriteTheDatabaseRefuses_KeepsThePanelOpenAndSaysSo()
    {
        await OpenForEditAsync(Mine);
        _albums.EditedName = "BBK Trip 2012";

        _repository.WriteFails = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new SqliteException("UNIQUE constraint failed", 19));

        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Contains("could not be saved", _albums.Status, StringComparison.Ordinal);
        Assert.True(_albums.IsEditing);
        Assert.False(_albums.IsBusy);
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

        // Named against the album rather than scanned for the word. A collection
        // has a Rename of its own now, one level up, and it is not a second way
        // to save this name - it is the only way to save a different one.
        Assert.DoesNotContain("Albums.RenameCommand", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Albums.SaveRuleCommand", markup, StringComparison.Ordinal);

        // One box types the album's name, so there is one place it can be
        // changed and one command that saves it.
        Assert.Equal(
            1, markup.Split("Albums.EditedName", StringSplitOptions.None).Length - 1);

        Assert.Contains(
            "Text=\"{Binding Albums.EditedName,", markup, StringComparison.Ordinal);
        Assert.Contains(
            "Command=\"{Binding Albums.SaveCommand}\"", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// One panel answers both questions, in the two modes.
    /// </summary>
    /// <remarks>
    /// Making an album and editing one ask the same things in the same order and
    /// refuse the same answers. Two panels meant two copies of that, and they had
    /// already drifted - only one of them said what a rule was for.
    /// </remarks>
    [Fact]
    public async Task TheSamePanelMakesAnAlbumAndEditsOne()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        Assert.True(_albums.IsEditing);
        Assert.True(_albums.IsNewAlbum);
        Assert.False(_albums.IsExistingAlbum);
        Assert.Equal("New album", _albums.PanelTitle);
        Assert.Equal("Create album", _albums.SaveLabel);
        Assert.Equal(string.Empty, _albums.EditedName);

        await OpenForEditAsync(Mine);

        Assert.True(_albums.IsEditing);
        Assert.False(_albums.IsNewAlbum);
        Assert.Equal("This album", _albums.PanelTitle);
        Assert.Equal("Save", _albums.SaveLabel);
    }

    /// <summary>
    /// A panel describing an album that does not exist offers nothing that can
    /// only be done to one that does.
    /// </summary>
    [Fact]
    public async Task ANewAlbumIsOfferedNothingToMoveOrRemove()
    {
        await _albums.StartCreatingCommand.ExecuteAsync(null);

        Assert.False(_albums.PanelOffersOriginals);
        Assert.False(_albums.PanelOffersProposal);

        await OpenForEditAsync(Mine);

        Assert.True(_albums.PanelOffersOriginals);
    }

    /// <summary>
    /// The panel's two halves and its one action row, read off the markup.
    /// </summary>
    /// <remarks>
    /// Layout is not behaviour, so nothing else here would catch the panel going
    /// back to one tall column with Save under the fold of it.
    /// </remarks>
    [Fact]
    public void ThePanelIsOneColumnOfIdentityAndOneOfRule()
    {
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        Assert.DoesNotContain("Albums.IsCreating", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Albums.NewName", markup, StringComparison.Ordinal);

        int panel = markup.IndexOf(
            "Visibility=\"{Binding Albums.IsEditing", StringComparison.Ordinal);
        Assert.InRange(panel, 1, markup.Length);

        string inThePanel = markup[panel..];
        Assert.Contains("Albums.PanelTitle", inThePanel, StringComparison.Ordinal);
        Assert.Contains("Albums.SaveLabel", inThePanel, StringComparison.Ordinal);
        Assert.Contains("Albums.CollectionOptions", inThePanel, StringComparison.Ordinal);
        Assert.Contains("AlbumRuleFields", inThePanel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Original files reads label, then the sentence, then the button - the order
    /// every other field on this panel uses.
    /// </summary>
    [Fact]
    public void TheSentenceComesBeforeTheButtonItExplains()
    {
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        int label = markup.IndexOf("Text=\"Original files\"", StringComparison.Ordinal);
        int sentence = markup.IndexOf(
            "Choose a folder inside their current photo source", StringComparison.Ordinal);
        int button = markup.IndexOf(
            "Content=\"Move originals to a folder...\"", StringComparison.Ordinal);

        Assert.InRange(label, 1, sentence);
        Assert.InRange(sentence, label, button);
    }

    /// <summary>
    /// The half of the panel that says what the album is scrolls when the window
    /// is too short for it, rather than leaving the screen.
    /// </summary>
    /// <remarks>
    /// The action row is docked to the bottom of that column, so the DockPanel
    /// measures it first and the fields above are given whatever is left. A bare
    /// stack asks for its full height anyway; the panel then wants more than the
    /// space it is centred in, and a surface centred in a space too short for it
    /// is laid out above the top of the screen. The name and the collection went
    /// with it, and no scrollbar existed to bring them back. None of that is
    /// behaviour, and it only shows on a short window, which is how it shipped.
    /// </remarks>
    [Fact]
    public void TheNameAndTheCollectionScrollRatherThanLeavingTheScreen()
    {
        XElement filled = IdentityColumn().Elements().Last();

        // The DockPanel fills with its last child, which is this one.
        Assert.Null(filled.Attribute("DockPanel.Dock"));

        Assert.Equal("ScrollViewer", filled.Name.LocalName);
        Assert.Equal("Auto", (string?)filled.Attribute("VerticalScrollBarVisibility"));

        Assert.Contains(
            filled.Descendants(s_wpf + "TextBox"),
            box => (string?)box.Attribute("AutomationProperties.Name") == "Name this album");
        Assert.Contains(
            filled.Descendants(s_wpf + "ComboBox"),
            box => (string?)box.Attribute("AutomationProperties.Name") == "Collection");
    }

    /// <summary>
    /// And Save is in none of the panel's scroll regions, or the fold would only
    /// have moved back to where the redesign found it.
    /// </summary>
    [Fact]
    public void SaveIsOutsideEveryScrollRegionOfThePanel()
    {
        XElement panel = AlbumPanel();

        XElement save = panel.Descendants(s_wpf + "Button").Single(
            button => (string?)button.Attribute("Content") == "{Binding Albums.SaveLabel}");

        Assert.DoesNotContain(
            "ScrollViewer",
            save.Ancestors().TakeWhile(element => element != panel)
                .Select(element => element.Name.LocalName));
    }

    /// <summary>The album panel, found by the one gate that opens it.</summary>
    private static XElement AlbumPanel() =>
        XDocument.Load(AppMarkup.PathTo("Shell", "MainWindow.xaml"))
            .Descendants(s_wpf + "Grid")
            .Single(grid => ((string?)grid.Attribute("Visibility"))
                ?.Contains("Albums.IsEditing", StringComparison.Ordinal) == true);

    /// <summary>The column that says what the album is, and what can be done to it.</summary>
    private static XElement IdentityColumn() =>
        AlbumPanel().Descendants(s_wpf + "DockPanel")
            .Single(column => (string?)column.Attribute("Grid.Column") == "0");

    /// <summary>Loads the wall, opens one album, and opens its edit panel.</summary>
    private async Task OpenForEditAsync(int albumId)
    {
        await _albums.ReloadAsync();
        _albums.ShowMine = albumId == Mine;
        _albums.Selected = _albums.Showing.Single(item => item.Id == albumId);

        await _albums.EditCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Waits on work the wall starts and does not hand back.
    /// </summary>
    /// <remarks>
    /// Decoding a cover is fire-and-forget, as it is in the app: the wall draws
    /// first and the pictures arrive when they arrive.
    /// </remarks>
    private static async Task WaitFor(Func<bool> done, string what)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (done())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    /// <summary>A real JPEG, because the cover is genuinely decoded.</summary>
    private static byte[] Jpeg()
    {
        BitmapSource source = BitmapSource.Create(
            8, 8, 96, 96, PixelFormats.Rgb24, null, new byte[8 * 8 * 3], 8 * 3);

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A cover the wall started decoding after the last assertion may
            // still have the file open. A temporary folder left behind is not
            // what any of these tests is about, and Windows will sweep it.
        }
    }

    /// <summary>
    /// Three albums, a record of what the panel asked to be done, and the ways
    /// a library can refuse to do it.
    /// </summary>
    private sealed class FakeAlbums : IAlbumRepository
    {
        /// <summary>What a read of the library throws, where it is to fail.</summary>
        public Exception? ReadFails { get; set; }

        /// <summary>What a read of one album's rule throws, on the same terms.</summary>
        public Exception? RuleReadFails { get; set; }

        /// <summary>What a write throws, for the tests about a refused save.</summary>
        public Exception? WriteFails { get; set; }

        /// <summary>The rule every album hands back, where one has been set.</summary>
        public AlbumRule? Rule { get; set; }

        /// <summary>Holds a rule read open, so a late answer can be tested.</summary>
        public TaskCompletionSource? Held { get; set; }

        /// <summary>
        /// Whether the album the user made has a cover for the wall to decode.
        /// </summary>
        /// <remarks>
        /// Off for the rest of these tests: a picture arriving in the middle of
        /// one is one more thing happening while a panel is being typed into,
        /// and only the tests about that arrival want it.
        /// </remarks>
        public bool HasCover { get; set; }

        /// <summary>How many times an album's photographs have been read.</summary>
        public int MemberReads { get; private set; }

        public List<(int AlbumId, string Name)> Renamed { get; } = [];

        public List<(int AlbumId, AlbumRule Rule)> RulesSet { get; } = [];

        public Task<IReadOnlyList<AlbumSummary>> GetAsync(
            CancellationToken cancellationToken = default)
        {
            if (ReadFails is not null)
            {
                return Task.FromException<IReadOnlyList<AlbumSummary>>(ReadFails);
            }

            return Task.FromResult<IReadOnlyList<AlbumSummary>>(
            [
                new AlbumSummary(
                    Mine, "BBK Trip", DateTime.UnixEpoch, DateTime.UnixEpoch,
                    AlbumKind.Trip, AlbumOrigin.Made, 179,
                    HasCover ? "cover" : null),
                new AlbumSummary(
                    Suggested, "12-16 March 2012", DateTime.UnixEpoch, DateTime.UnixEpoch,
                    AlbumKind.Trip, AlbumOrigin.Proposed, 41, CoverThumbnailName: null),
                new AlbumSummary(
                    Shelved, "Weekend hike", DateTime.UnixEpoch, DateTime.UnixEpoch,
                    AlbumKind.Trip, AlbumOrigin.Made, 41, CoverThumbnailName: null,
                    CollectionId: Weekends),
            ]);
        }

        public Task RenameAsync(
            int albumId, string name, CancellationToken cancellationToken = default)
        {
            if (WriteFails is not null)
            {
                return Task.FromException(WriteFails);
            }

            Renamed.Add((albumId, name));
            return Task.CompletedTask;
        }

        public Task SetRuleAsync(
            int albumId, AlbumRule rule, CancellationToken cancellationToken = default)
        {
            RulesSet.Add((albumId, rule));
            return Task.CompletedTask;
        }

        public async Task<AlbumRule> GetRuleAsync(
            int albumId, CancellationToken cancellationToken = default)
        {
            if (Held is not null)
            {
                await Held.Task.ConfigureAwait(false);
            }

            if (RuleReadFails is not null)
            {
                throw RuleReadFails;
            }

            return Rule ?? AlbumRule.None;
        }

        public Task<IReadOnlyList<int>> GetMembersAsync(
            int albumId, CancellationToken cancellationToken = default)
        {
            MemberReads++;
            return Task.FromResult<IReadOnlyList<int>>([]);
        }

        public Task<int> CreateAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

    /// <summary>One shelf, with the album that stands on it.</summary>
    /// <remarks>
    /// The list of cover names is built again on every read, as the real
    /// repository's is. That is the whole point of this double: a record holds
    /// that list by reference, so re-reading the band hands the wall a new row
    /// for a shelf nobody has left - and an empty collection expression would
    /// not, because the compiler is free to give back the same cached instance
    /// every time and the row would then compare equal.
    ///
    /// <para>Left empty so the band decodes no mosaic, which keeps the one
    /// decode in this file the album cover the tests below are about.</para>
    /// </remarks>
    private sealed class OneShelf : ICollectionRepository
    {
        public Task<IReadOnlyList<CollectionSummary>> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollectionSummary>>(
            [
                new CollectionSummary(
                    Weekends, "Weekends away", AlbumCount: 1, PhotoCount: 41,
                    CoverThumbnailNames: new List<string>()),
            ]);

        public Task<int> CreateAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RenameAsync(
            int collectionId, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(int collectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CollectionFillResult> SetAlbumsAsync(
            int collectionId,
            IReadOnlyList<int> albumIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>
        /// Answers rather than throws, because the album panel calls this
        /// whenever its Collection field changed - and here it did not.
        /// </summary>
        public Task<string?> SetAlbumCollectionAsync(
            int albumId,
            int? collectionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    /// <summary>
    /// The real store, with its one tile handed over only when the test says so.
    /// </summary>
    /// <remarks>
    /// The wall decodes on the thread pool, so without a gate the test would be
    /// racing the decode for the very moment it is about - the picture arriving
    /// after the name has been typed. Holding the decode inside the store makes
    /// that order a fact rather than a hope.
    ///
    /// <para>Everything else is passed straight through rather than refused, so
    /// a test added to this file later that does ask the store for something
    /// gets the real answer.</para>
    /// </remarks>
    private sealed class HeldCover : IThumbnailStore
    {
        private readonly TaskCompletionSource _released = new();
        private readonly IThumbnailStore _inner;
        private readonly string _tile;

        public HeldCover(IThumbnailStore inner, string tile)
        {
            _inner = inner;
            _tile = tile;
        }

        /// <summary>Lets the cover the wall is waiting on finish decoding.</summary>
        public void Release() => _released.TrySetResult();

        public string ResolveTilePath(string thumbnailName)
        {
            // A timeout rather than a wait for ever, so a test that forgets to
            // release the cover fails on its own assertion instead of hanging
            // the whole run.
            _ = _released.Task.Wait(TimeSpan.FromSeconds(10));
            return _tile;
        }

        public Task<string> SaveAsync(
            GeneratedThumbnail thumbnail, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(thumbnail, cancellationToken);

        public string NameFor(string contentHash) => _inner.NameFor(contentHash);

        public string ResolvePreviewPath(string thumbnailName) =>
            _inner.ResolvePreviewPath(thumbnailName);

        public bool Exists(string? thumbnailName) => _inner.Exists(thumbnailName);

        public DateTime? PreviewWrittenUtc(string? thumbnailName) =>
            _inner.PreviewWrittenUtc(thumbnailName);

        public bool TryDelete(string? thumbnailName) => _inner.TryDelete(thumbnailName);

        public IReadOnlyCollection<string> ListStoredNames() => _inner.ListStoredNames();

        public void RemoveEmptyShards() => _inner.RemoveEmptyShards();
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

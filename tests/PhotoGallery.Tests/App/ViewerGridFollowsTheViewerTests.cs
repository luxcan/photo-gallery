using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
using PhotoGallery.App.Albums;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Search;
using PhotoGallery.Infrastructure;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The screen a photograph was opened from keeping up with what is done to it
/// in the viewer.
/// </summary>
/// <remarks>
/// The library's wall already followed the viewer: a photograph filed or named
/// while it was open was checked against the wall's question once the viewer
/// left it. Every other screen that opens the viewer was left behind. Moving a
/// photograph from one album to another left it on the first album's page until
/// the page was closed and opened again - the bug as it was reported - and the
/// album's proposals, a person's page and the cards cut from a turned picture
/// all had the same hole.
///
/// <para>Against the real service graph, real renditions and a real file,
/// because each screen's answer to "does this photograph still belong here?" is
/// that screen's own query, and a fake would test only that it was asked. The
/// last few tests are the smaller catches around the viewer: counts that a
/// delete left behind, an inspector left open, and the viewer's own panel after
/// the library changed under it.</para>
/// </remarks>
public sealed class ViewerGridFollowsTheViewerTests : IDisposable
{
    /// <summary>Six photographs of 2020: three in Bali, one in Sentosa, two loose.</summary>
    private const int Photographs = 6;

    /// <summary>Two copies of one picture, from 2021, in no album.</summary>
    private const int FirstCopy = 7;
    private const int SecondCopy = 8;

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    private int _bali;
    private int _sentosa;

    public ViewerGridFollowsTheViewerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-grid-follows-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _services = new ServiceCollection()
            .AddPhotoGalleryInfrastructure(_root)
            .AddPhotoGalleryHandlers()
            .BuildServiceProvider();

        using (IServiceScope scope = _services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<GalleryDbContext>().Database.Migrate();
        }

        Seed();

        var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
        var thumbnails = _services.GetRequiredService<IThumbnailStore>();

        _main = new MainViewModel(
            scopeFactory,
            new GalleryViewModel(scopeFactory, thumbnails),
            new PeopleViewModel(scopeFactory, thumbnails),
            new DuplicatesViewModel(scopeFactory, thumbnails),
            thumbnails,
            new SilentLog());

        _main.Gallery.SetColumns(4);
        _main.Albums.SetColumns(4);
        _main.People.SetColumns(4);
    }

    // ------------------------------------------------------------ album page

    /// <summary>
    /// Moved to another album from the viewer, a photograph leaves the album's
    /// page once the viewer is closed, and both albums' cards count it where it
    /// now is - the bug as it was reported.
    /// </summary>
    /// <remarks>
    /// Chosen from the list, the way somebody files into an album that already
    /// exists. The cards are part of what was reported: the page alone could be
    /// right while the wall behind it still counted the photograph in Bali.
    /// </remarks>
    [Fact]
    public async Task MovedToAnotherAlbum_LeavesTheAlbumPageWhenTheViewerCloses()
    {
        await OpenAlbumAsync("Bali", 3);
        GalleryTile moved = AlbumTile(2);

        await OpenFromAlbumAsync(moved);
        await ChooseAlbumForOpenPhotoAsync("Sentosa");

        _main.Gallery.ClosePhotoCommand.Execute(null);
        await _main.Gallery.WallSettled;

        Assert.DoesNotContain(moved, _main.Albums.Photos.Tiles);
        Assert.Equal(2, _main.Albums.PhotoCount);

        await TestWait.ForAsync(
            () => Card("Bali").Summary.PhotoCount == 2 && Card("Sentosa").Summary.PhotoCount == 2,
            "both albums' cards to count the photograph where it now is",
            attempts: 1000);
    }

    /// <summary>
    /// But not while the viewer is still on it, so its arrows still know where
    /// they are; stepping on is what takes it off.
    /// </summary>
    [Fact]
    public async Task MovedToAnotherAlbum_StaysWhileTheViewerIsOnIt_AndNextStillSteps()
    {
        await OpenAlbumAsync("Bali", 3);
        GalleryTile moved = _main.Albums.Photos[0];
        GalleryTile after = _main.Albums.Photos[1];

        await OpenFromAlbumAsync(moved);
        await FileOpenPhotoAsync("Sentosa");
        await _main.Gallery.WallSettled;

        Assert.Contains(moved, _main.Albums.Photos.Tiles);
        Assert.True(_main.Gallery.NextPhotoCommand.CanExecute(null));

        _main.Gallery.NextPhotoCommand.Execute(null);
        await _main.Gallery.WallSettled;

        Assert.Same(after, _main.Gallery.OpenTile);
        Assert.DoesNotContain(moved, _main.Albums.Photos.Tiles);
    }

    /// <summary>
    /// The page loses the one photograph and nothing else: every other tile is
    /// the very object it was, so nothing was read again and the page did not
    /// jump back to the top.
    /// </summary>
    [Fact]
    public async Task MovedToAnotherAlbum_OtherTilesAreTheSameObjects()
    {
        await OpenAlbumAsync("Bali", 3);
        GalleryTile moved = AlbumTile(2);
        List<GalleryTile> others = [.. _main.Albums.Photos.Tiles.Where(tile => !ReferenceEquals(tile, moved))];

        await OpenFromAlbumAsync(moved);
        await FileOpenPhotoAsync("Sentosa");

        _main.Gallery.ClosePhotoCommand.Execute(null);
        await _main.Gallery.WallSettled;

        Assert.Equal(others, _main.Albums.Photos.Tiles);
    }

    /// <summary>
    /// Taking the last photograph out leaves the page empty, and says so.
    /// </summary>
    [Fact]
    public async Task TakingOutTheLastPhoto_EmptiesTheAlbumPage()
    {
        await OpenAlbumAsync("Sentosa", 1);
        List<string?> told = [];
        _main.Albums.PropertyChanged += (_, e) => told.Add(e.PropertyName);

        await OpenFromAlbumAsync(_main.Albums.Photos[0]);
        await _main.Gallery.AddToAlbumCommand.ExecuteAsync(null);
        await _main.Gallery.Albums.TakeOutCommand.ExecuteAsync(null);

        _main.Gallery.ClosePhotoCommand.Execute(null);
        await _main.Gallery.WallSettled;

        Assert.False(_main.Albums.HasPhotos);
        Assert.Equal(0, _main.Albums.PhotoCount);

        // Said, not only true: the hint that the album is empty is bound to it.
        Assert.Contains(nameof(AlbumsViewModel.HasPhotos), told);
    }

    /// <summary>
    /// Deleted from an album's page, a photograph's tile leaves that page rather
    /// than staying as a picture the library no longer has.
    /// </summary>
    [Fact]
    public async Task DeletedFromTheAlbumPage_ItsTileLeaves()
    {
        await OpenAlbumAsync("Bali", 3);
        GalleryTile deleted = AlbumTile(2);

        await OpenFromAlbumAsync(deleted);
        await DeleteOpenPhotoAsync();

        Assert.Null(_main.Gallery.OpenTile);
        Assert.DoesNotContain(deleted, _main.Albums.Photos.Tiles);
        Assert.Equal(2, _main.Albums.PhotoCount);
    }

    /// <summary>
    /// A write that lands after the viewer has closed is still settled against
    /// the grid it was opened from.
    /// </summary>
    /// <remarks>
    /// Filing is quick but not instant, and the viewer can be closed before the
    /// write returns. The grid is the one captured before the write, so the
    /// page is put right even though the viewer holds no grid by then. Landed
    /// here without racing a real write, by marking after the close.
    /// </remarks>
    [Fact]
    public async Task AWriteLandingAfterTheViewerClosed_IsSettledAgainstTheGridItWasOpenedFrom()
    {
        await OpenAlbumAsync("Bali", 3);
        GalleryTile moved = AlbumTile(2);

        await OpenFromAlbumAsync(moved);
        _main.Gallery.ClosePhotoCommand.Execute(null);

        await MoveDirectlyAsync(2, _sentosa);
        _main.Gallery.ChangedWhileOpen(2, _main.Albums);
        await _main.Gallery.WallSettled;

        Assert.DoesNotContain(moved, _main.Albums.Photos.Tiles);
    }

    /// <summary>
    /// An album left open while photographs were deleted on Library is read
    /// again on the way back.
    /// </summary>
    /// <remarks>
    /// Coming back reads the albums again but finds the open one under the guard
    /// that stops a re-read from counting as opening it, so its photographs were
    /// never read and the deleted one stayed on the page.
    /// </remarks>
    [Fact]
    public async Task BatchDeletedWhileAnAlbumWasLeftOpen_ItsGridIsReadOnReturn()
    {
        await OpenAlbumAsync("Bali", 3);

        ShowSection(ActivitySection.LibraryKey);
        PhotoRemovalResult result = await DeleteDirectlyAsync(2);
        await _main.Gallery.AfterChosenDeletedAsync(result);

        ShowSection(ActivitySection.AlbumsKey);

        await TestWait.ForAsync(
            () => _main.Albums.PhotoCount == 2,
            "the album left open to be read again",
            attempts: 1000);
        Assert.DoesNotContain(_main.Albums.Photos.Tiles, tile => tile.Item.Id == 2);
    }

    /// <summary>
    /// An album page told it may be out of date, and found to hold exactly what
    /// it shows, is left as it was rather than filled again.
    /// </summary>
    /// <remarks>
    /// Every change made on another section marks the page - a face named, a
    /// photograph turned - and refilling it on the way back put somebody who had
    /// scrolled down through a large album back at the top for nothing.
    /// </remarks>
    [Fact]
    public async Task AStalePageThatHasNotChanged_KeepsItsTiles()
    {
        await OpenAlbumAsync("Bali", 3);
        List<GalleryTile> before = [.. _main.Albums.Photos.Tiles];

        _main.Albums.MarkPhotosStale();
        await _main.Albums.ReadStalePhotosAsync();

        Assert.Equal(before, _main.Albums.Photos.Tiles);
    }

    /// <summary>
    /// A page marked out of date is never refilled by a reload asked for while
    /// the viewer is open over it.
    /// </summary>
    /// <remarks>
    /// The mark used to be acted on inside the albums' reload, and a turn made
    /// in the viewer asks for one. Left over from a visit whose own reload was
    /// turned away as busy, it refilled the page under the open viewer: the tile
    /// on screen was replaced, and the arrows went dead.
    /// </remarks>
    [Fact]
    public async Task AStaleMarkLeftOver_IsNotActedOnUnderAnOpenViewer()
    {
        await ShowAlbumsAsync();
        await TestWait.ForAsync(
            () => _main.Albums.Wall.All(album => album.Cover is not null),
            "the covers to be decoded",
            attempts: 1000);

        AlbumItem card = Card("Bali");
        string cover = card.Summary.CoverThumbnailName!;

        await OpenAlbumAsync("Bali", 3);
        GalleryTile open = _main.Albums.Photos.Tiles.Single(tile => tile.ThumbnailName == cover);
        await OpenFromAlbumAsync(open);

        // Moved under the page, as another computer's answers might, so a
        // re-read would genuinely refill it rather than find nothing to do.
        await MoveDirectlyAsync(open.Item.Id == 1 ? 2 : 1, _sentosa);
        _main.Albums.MarkPhotosStale();

        await _main.Gallery.TurnPhotoCommand.ExecuteAsync("90");
        await TestWait.ForAsync(
            () => Card("Bali").Cover is { } redrawn && !ReferenceEquals(redrawn, card.Cover)
                && !_main.Albums.IsBusy,
            "the albums behind the viewer to be read again",
            attempts: 1000);

        Assert.Contains(open, _main.Albums.Photos.Tiles);
        Assert.True(
            _main.Gallery.NextPhotoCommand.CanExecute(null)
            || _main.Gallery.PreviousPhotoCommand.CanExecute(null),
            "the arrows went dead under the open viewer");
    }

    // ------------------------------------------------------------- people

    /// <summary>
    /// A face given to somebody else takes the photograph off the first person's
    /// page once the viewer closes, and the count and the heading drop with it.
    /// </summary>
    /// <remarks>
    /// Written directly rather than through the viewer's name list: naming a face
    /// that is already confirmed as somebody adds a second confirmation and
    /// leaves the first, so the list cannot take a face off anybody. Marked the
    /// way the viewer marks a write it made, with the grid it was opened from.
    /// </remarks>
    [Fact]
    public async Task GivenToSomeoneElse_LeavesThePersonsPage_AndCountAndHeadingDrop()
    {
        await OpenPersonAsync("Ana", 3);
        GalleryTile given = PersonTile(2);

        await OpenFromPersonAsync(given, faces: 1);
        await GiveFaceToAsync(_main.Gallery.OpenFaces[0].FaceId, "Ben");
        _main.Gallery.ChangedWhileOpen(2, _main.People);

        _main.Gallery.ClosePhotoCommand.Execute(null);
        await _main.Gallery.WallSettled;

        Assert.DoesNotContain(given, _main.People.Photos.Tiles);
        Assert.Equal(2, _main.People.PhotoCount);
        Assert.Equal("2 pictures", Assert.Single(_main.People.PhotoRows).HeadingDetail);
    }

    /// <summary>
    /// A face set aside takes the photograph off the person's page, and the
    /// list of people behind the viewer counts it out at once.
    /// </summary>
    /// <remarks>
    /// Setting a face aside unassigns it, and nothing told the People screen:
    /// the person's count stayed where it was until the section was left and
    /// opened again.
    /// </remarks>
    [Fact]
    public async Task IgnoredOnAPersonsPage_LeavesIt_AndTheListCountDrops()
    {
        await OpenPersonAsync("Ana", 3);
        GalleryTile ignored = PersonTile(2);

        await OpenFromPersonAsync(ignored, faces: 1);
        _main.Gallery.BeginNamingFaceCommand.Execute(_main.Gallery.OpenFaces[0]);
        await _main.Gallery.Picker.SetAsideCommand.ExecuteAsync(null);

        await TestWait.ForAsync(
            () => _main.People.Named.Single(person => person.DisplayName == "Ana").Summary.Photos == 2,
            "the list behind the viewer to count Ana again",
            attempts: 1000);

        _main.Gallery.ClosePhotoCommand.Execute(null);
        await _main.Gallery.WallSettled;

        Assert.DoesNotContain(ignored, _main.People.Photos.Tiles);
        Assert.Equal(2, _main.People.PhotoCount);
    }

    /// <summary>
    /// Naming somebody from a person's page leaves the viewer's arrows working.
    /// </summary>
    /// <remarks>
    /// Naming tells the shell the library changed, and the shell rebuilt the
    /// People screen - person's page and all - behind the open viewer. Every
    /// tile was replaced, so the one on screen was no longer in the grid and
    /// next and previous found nothing to step to.
    /// </remarks>
    [Fact]
    public async Task NamingFromAPersonsPage_KeepsTheArrowsWorking()
    {
        await OpenPersonAsync("Ana", 3);
        GalleryTile group = PersonTile(3);

        await OpenFromPersonAsync(group, faces: 2);
        await NameFaceAsync(
            _main.Gallery.OpenFaces.Single(face => face.Face.PersonName is null), "Ben");

        // The list behind the viewer has been rebuilt with the new name on it.
        await TestWait.ForAsync(
            () => _main.People.Named.Any(person => person.DisplayName == "Ben") && !_main.People.IsBusy,
            "the People screen to be rebuilt behind the viewer",
            attempts: 1000);

        Assert.Contains(group, _main.People.Photos.Tiles);
        Assert.True(
            _main.Gallery.NextPhotoCommand.CanExecute(null)
            || _main.Gallery.PreviousPhotoCommand.CanExecute(null),
            "the arrows went dead under the open viewer");
    }

    /// <summary>
    /// A merge of another computer's answers, landing while the viewer is open
    /// over a person's page, leaves the viewer's arrows working.
    /// </summary>
    /// <remarks>
    /// The shell already left the page alone for a change the viewer reported,
    /// but a merge reloaded People with the page and all, so every tile was
    /// replaced and the one on screen was no longer in the grid.
    /// </remarks>
    [Fact]
    public async Task AMergeUnderTheViewerOnAPersonsPage_KeepsTheArrowsWorking()
    {
        await OpenPersonAsync("Ana", 3);
        GalleryTile open = PersonTile(2);
        await OpenFromPersonAsync(open, faces: 1);

        // The reload starts before this returns, so once People is no longer
        // busy it has finished rather than not yet begun.
        _main.LibraryChangedByShare();

        await TestWait.ForAsync(
            () => !_main.People.IsBusy,
            "the People screen to be read again behind the viewer",
            attempts: 1000);

        Assert.Contains(open, _main.People.Photos.Tiles);
        Assert.True(
            _main.Gallery.NextPhotoCommand.CanExecute(null)
            || _main.Gallery.PreviousPhotoCommand.CanExecute(null),
            "the arrows went dead under the open viewer");
    }

    // ---------------------------------------------------- suggestions strip

    /// <summary>
    /// A proposal filed into another album from the viewer leaves the strip,
    /// and keeping the ones still on does not drag it back.
    /// </summary>
    [Fact]
    public async Task SuggestionFiledElsewhere_LeavesTheStrip_AndKeepDoesNotReAddIt()
    {
        await SuggestForBaliAsync();
        GalleryTile filed = SuggestionTile(5);

        _main.OpenSuggestedPhotoCommand.Execute(filed);
        await FileOpenPhotoAsync("Sentosa");

        _main.Gallery.ClosePhotoCommand.Execute(null);
        await _main.Gallery.WallSettled;

        Assert.DoesNotContain(filed, _main.Albums.Suggestions);
        Assert.Equal(1, _main.Albums.SuggestionGrid.Count);

        await _main.Albums.KeepSuggestionsCommand.ExecuteAsync(null);

        Assert.Equal(_sentosa, (await AlbumOfAsync(5))?.Id);
        Assert.Equal(_bali, (await AlbumOfAsync(6))?.Id);
    }

    /// <summary>
    /// A proposal deleted from the viewer leaves the strip, so keeping the rest
    /// does not hand the album a photograph the library no longer has.
    /// </summary>
    [Fact]
    public async Task SuggestionDeleted_LeavesTheStrip()
    {
        await SuggestForBaliAsync();
        GalleryTile deleted = SuggestionTile(5);

        _main.OpenSuggestedPhotoCommand.Execute(deleted);
        await DeleteOpenPhotoAsync();

        Assert.DoesNotContain(deleted, _main.Albums.Suggestions);
        Assert.Equal(1, _main.Albums.SuggestionGrid.Count);
        Assert.StartsWith("1 photograph left", _main.Albums.SuggestionNote, StringComparison.Ordinal);
    }

    /// <summary>
    /// A proposal filed into the very album that offered it, through the
    /// viewer's album list, leaves the strip and joins the album's page once
    /// the viewer closes.
    /// </summary>
    /// <remarks>
    /// The strip let go of it, and nothing told the page behind - so it was in
    /// neither place on screen until the album was opened again.
    /// </remarks>
    [Fact]
    public async Task SuggestionFiledIntoTheOpenAlbum_JoinsItsPageWhenTheViewerCloses()
    {
        await SuggestForBaliAsync();
        GalleryTile filed = SuggestionTile(5);

        _main.OpenSuggestedPhotoCommand.Execute(filed);
        await ChooseAlbumForOpenPhotoAsync("Bali");

        _main.Gallery.ClosePhotoCommand.Execute(null);
        await _main.Gallery.WallSettled;

        Assert.DoesNotContain(filed, _main.Albums.Suggestions);
        await TestWait.ForAsync(
            () => _main.Albums.PhotoCount == 4
                && _main.Albums.Photos.Tiles.Any(tile => tile.Item.Id == 5),
            "the album's page to take in the proposal filed into it",
            attempts: 1000);
    }

    // --------------------------------------------------------------- turn

    /// <summary>
    /// Turning the photograph an album shows for itself redraws that album's card
    /// behind the viewer.
    /// </summary>
    [Fact]
    public async Task TurningACover_RedrawsTheAlbumCardBehindTheViewer()
    {
        await ShowAlbumsAsync();
        await TestWait.ForAsync(
            () => _main.Albums.Wall.All(album => album.Cover is not null),
            "the covers to be decoded",
            attempts: 1000);

        AlbumItem card = Card("Bali");
        string cover = card.Summary.CoverThumbnailName!;

        await OpenAlbumAsync("Bali", 3);
        await OpenFromAlbumAsync(_main.Albums.Photos.Tiles.Single(tile => tile.ThumbnailName == cover));
        await _main.Gallery.TurnPhotoCommand.ExecuteAsync("90");

        await TestWait.ForAsync(
            () => Card("Bali").Cover is { } redrawn && !ReferenceEquals(redrawn, card.Cover),
            "the card behind the viewer to be redrawn",
            attempts: 1000);
    }

    /// <summary>
    /// Turning one copy of a picture redraws every copy of it on the wall.
    /// </summary>
    /// <remarks>
    /// The turn is made to the rendition, and copies share one - so the copy
    /// beside it stayed upside down until the whole wall was read again.
    /// </remarks>
    [Fact]
    public async Task TurningRepaintsEveryCopyOnTheWall()
    {
        await _main.Gallery.LoadAsync();
        GalleryTile copy = WallTile(FirstCopy);
        Assert.NotNull(copy.Picture);
        object before = copy.Picture!;

        _main.Gallery.OpenPhotoCommand.Execute(WallTile(SecondCopy));
        await _main.Gallery.TurnPhotoCommand.ExecuteAsync("90");

        Assert.NotNull(copy.Picture);
        Assert.NotSame(before, copy.Picture);
        Assert.True(copy.IsTurnedInAppOnly);
    }

    /// <summary>
    /// "Turned here only" is still said after stepping away and back.
    /// </summary>
    /// <remarks>
    /// It was read from the row the tile was made from, which does not change
    /// for the life of the tile - so the line vanished on the way back, and on
    /// the library's wall stayed gone even on the next visit.
    /// </remarks>
    [Fact]
    public async Task TurnNoticeSurvivesSteppingAwayAndBack()
    {
        await _main.Gallery.LoadAsync();
        _main.Gallery.OpenPhotoCommand.Execute(WallTile(5));

        await _main.Gallery.TurnPhotoCommand.ExecuteAsync("90");
        Assert.Equal(TurnNotices.HereOnly, _main.Gallery.TurnNotice);

        _main.Gallery.NextPhotoCommand.Execute(null);
        _main.Gallery.PreviousPhotoCommand.Execute(null);

        Assert.Equal(5, _main.Gallery.OpenTile?.Item.Id);
        Assert.Equal(TurnNotices.HereOnly, _main.Gallery.TurnNotice);
    }

    /// <summary>
    /// Copies of one picture whose files came out of a turn differently are
    /// each told what happened to their own file.
    /// </summary>
    /// <remarks>
    /// One copy's file can take the orientation tag while the other's cannot.
    /// Every copy used to be given the verdict for the turn as a whole - "not
    /// here only", because one file was told - so stepping onto the copy whose
    /// file was not told said nothing, though it is only upright in this app.
    /// </remarks>
    [Fact]
    public async Task TurningCopiesWithDifferentFiles_TellsEachCopyAboutItsOwnFile()
    {
        WriteJpegWithOrientation(Path.Combine(_root, "copies", "a.jpg"));

        await _main.Gallery.LoadAsync();
        GalleryTile told = WallTile(FirstCopy);
        GalleryTile notTold = WallTile(SecondCopy);

        _main.Gallery.OpenPhotoCommand.Execute(notTold);
        await _main.Gallery.TurnPhotoCommand.ExecuteAsync("90");

        Assert.False(told.IsTurnedInAppOnly);
        Assert.True(notTold.IsTurnedInAppOnly);
        Assert.Equal(TurnNotices.HereOnly, _main.Gallery.TurnNotice);
    }

    // ------------------------------------------------ around the viewer

    /// <summary>
    /// Deleting from the viewer puts the folder tree's count right.
    /// </summary>
    [Fact]
    public async Task DeletingFromTheViewer_TheFolderCountDrops()
    {
        await _main.Gallery.LoadFoldersAsync();
        await _main.Gallery.LoadAsync();
        Assert.Equal(Photographs, Folder("2020").ItemCount);

        _main.Gallery.OpenPhotoCommand.Execute(WallTile(5));
        await DeleteOpenPhotoAsync();

        Assert.Equal(Photographs - 1, Folder("2020").ItemCount);
    }

    /// <summary>
    /// Deleting from the viewer keeps the folder tree's own folders, so the tree
    /// on screen stays open where it was and keeps the folder chosen.
    /// </summary>
    /// <remarks>
    /// The tree was read again by replacing every folder in it, and the tree on
    /// screen keeps which folders are open against the folders it was given -
    /// so every delete folded it back to its roots and lost the highlight.
    /// </remarks>
    [Fact]
    public async Task DeletingFromTheViewer_KeepsTheFolderTreesFolders()
    {
        await _main.Gallery.LoadFoldersAsync();
        await _main.Gallery.LoadAsync();
        List<FolderItem> roots = [.. _main.Gallery.Folders];
        FolderItem year = Folder("2020");

        _main.Gallery.OpenPhotoCommand.Execute(WallTile(5));
        await DeleteOpenPhotoAsync();

        Assert.Equal(roots, _main.Gallery.Folders);
        Assert.Same(year, Folder("2020"));
        Assert.Equal(Photographs - 1, year.ItemCount);
    }

    /// <summary>
    /// Deleting from the viewer puts the Photo sources table's count right.
    /// </summary>
    [Fact]
    public async Task DeletingFromTheViewer_TheSourceCountDrops()
    {
        int total = Photographs + 2;
        var source = new PhotoSourceItem(1, _root, total, new DateTime(2026, 1, 1));
        _main.Sources.Add(source);

        await _main.Gallery.LoadAsync();
        _main.Gallery.OpenPhotoCommand.Execute(WallTile(5));
        await DeleteOpenPhotoAsync();

        await TestWait.ForAsync(
            () => source.PhotoCount == total - 1,
            "the source's count to be read again",
            attempts: 1000);
    }

    /// <summary>
    /// The copy being inspected on Duplicates is put down on leaving the
    /// section, as the viewer is.
    /// </summary>
    [Fact]
    public async Task LeavingDuplicates_PutsTheInspectedCopyDown()
    {
        var set = new DuplicateSetItem(new DuplicateSetView(
            1,
            DuplicateKind.Near,
            [
                .. Enumerable.Range(0, 2).Select(at => new DuplicateCopy(
                    AssetId: at + 1,
                    PhotoSourceId: 1,
                    RelativePath: $@"2020\{at}.jpg",
                    FullPath: Path.Combine(_root, "2020", $"{at}.jpg"),
                    ThumbnailName: null,
                    Length: 1,
                    Width: 100,
                    Height: 100,
                    Role: at == 0 ? DuplicateRole.Keeper : DuplicateRole.Redundant,
                    Distance: at)),
            ]));

        _main.Duplicates.ShowNear = true;
        _main.Duplicates.Near.Add(set);
        await _main.Duplicates.InspectCopyCommand.ExecuteAsync(set.Copies[1]);
        Assert.True(_main.Duplicates.IsInspecting);

        ShowSection(ActivitySection.LibraryKey);

        Assert.False(_main.Duplicates.IsInspecting);
    }

    /// <summary>
    /// What the viewer says about the open photograph can be read again without
    /// stepping away from it, which is what a merge of another computer's
    /// answers asks for.
    /// </summary>
    [Fact]
    public async Task RereadingTheOpenPhoto_PicksUpAnAlbumWrittenUnderIt()
    {
        await _main.Gallery.LoadAsync();
        _main.Gallery.OpenPhotoCommand.Execute(WallTile(5));
        await _main.Gallery.RereadOpenPhotoAsync();
        Assert.Null(_main.Gallery.OpenPhotoAlbum);

        await MoveDirectlyAsync(5, _sentosa);
        await _main.Gallery.RereadOpenPhotoAsync();

        Assert.Equal("Sentosa", _main.Gallery.OpenPhotoAlbum?.Name);
    }

    // ------------------------------------------------------------- helpers

    private void ShowSection(string key) =>
        _main.SelectedSection = _main.TopSections.Single(section => section.Key == key);

    private async Task ShowAlbumsAsync()
    {
        ShowSection(ActivitySection.AlbumsKey);
        await TestWait.ForAsync(
            () => _main.Albums.Wall.Count == 2 && !_main.Albums.IsBusy,
            "the albums to be read",
            attempts: 1000);
    }

    private AlbumItem Card(string name) => _main.Albums.Wall.Single(album => album.Name == name);

    private async Task OpenAlbumAsync(string name, int photographs)
    {
        if (!_main.ShowAlbums)
        {
            await ShowAlbumsAsync();
        }

        _main.Albums.OpenCommand.Execute(Card(name));
        await TestWait.ForAsync(
            () => _main.Albums.Selected?.Name == name && _main.Albums.PhotoCount == photographs,
            $"{name}'s photographs to be read",
            attempts: 1000);
    }

    private GalleryTile AlbumTile(int assetId) =>
        _main.Albums.Photos.Tiles.Single(tile => tile.Item.Id == assetId);

    /// <summary>Opens one of the album's photographs, waiting for its album line.</summary>
    private async Task OpenFromAlbumAsync(GalleryTile tile)
    {
        _main.OpenAlbumPhotoCommand.Execute(tile);
        await TestWait.ForAsync(
            () => _main.Gallery.OpenPhotoAlbum is not null,
            "the open photograph's album to be read",
            attempts: 1000);
    }

    private async Task OpenPersonAsync(string name, int photographs)
    {
        ShowSection(ActivitySection.PeopleKey);
        await TestWait.ForAsync(
            () => _main.People.Named.Any(person => person.DisplayName == name) && !_main.People.IsBusy,
            "the people to be read",
            attempts: 1000);

        _main.People.SelectedPerson = _main.People.Named.Single(person => person.DisplayName == name);
        await TestWait.ForAsync(
            () => _main.People.PhotoCount == photographs && _main.People.Photos.Count == photographs,
            $"{name}'s pictures to be read",
            attempts: 1000);
    }

    private GalleryTile PersonTile(int assetId) =>
        _main.People.Photos.Tiles.Single(tile => tile.Item.Id == assetId);

    /// <summary>Opens one of the person's pictures with its faces drawn.</summary>
    private async Task OpenFromPersonAsync(GalleryTile tile, int faces)
    {
        _main.Gallery.ShowFaceNames = true;
        _main.OpenPersonPhotoCommand.Execute(tile);
        await TestWait.ForAsync(
            () => _main.Gallery.OpenFaces.Count == faces,
            "its faces to be drawn",
            attempts: 1000);
    }

    private async Task NameFaceAsync(PhotoFaceItem face, string name)
    {
        _main.Gallery.BeginNamingFaceCommand.Execute(face);
        _main.Gallery.Picker.Typed = name;
        await _main.Gallery.Picker.AddCommand.ExecuteAsync(null);
    }

    /// <summary>Takes a face off whoever it was and confirms it as somebody else.</summary>
    private async Task GiveFaceToAsync(int faceId, string name)
    {
        using IServiceScope scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPeopleRepository>().UnassignAsync([faceId]);
        await scope.ServiceProvider.GetRequiredService<AssignFacesHandler>().HandleAsync(
            new AssignFacesRequest([faceId], AssignmentSource.Confirmed, DisplayName: name));
    }

    /// <summary>Gives Bali a rule for 2020 and asks it for proposals: the two loose ones.</summary>
    private async Task SuggestForBaliAsync()
    {
        using (IServiceScope scope = _services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAlbumRepository>().SetRuleAsync(
                _bali,
                new AlbumRule(new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31), [], []));
        }

        await OpenAlbumAsync("Bali", 3);
        await _main.Albums.SuggestCommand.ExecuteAsync(null);

        Assert.Equal([5, 6], _main.Albums.Suggestions.Select(tile => tile.Item.Id).Order());
    }

    private GalleryTile SuggestionTile(int assetId) =>
        _main.Albums.Suggestions.Single(tile => tile.Item.Id == assetId);

    private GalleryTile WallTile(int assetId) =>
        _main.Gallery.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Item.Id == assetId);

    private FolderItem Folder(string relative)
    {
        static IEnumerable<FolderItem> All(IEnumerable<FolderItem> nodes) =>
            nodes.SelectMany(node => All(node.Children).Prepend(node));

        return All(_main.Gallery.Folders).Single(node => node.RelativeFolder == relative);
    }

    /// <summary>
    /// Files the open photograph into an album that already exists, by choosing
    /// it from the viewer's list.
    /// </summary>
    private async Task ChooseAlbumForOpenPhotoAsync(string album)
    {
        await _main.Gallery.AddToAlbumCommand.ExecuteAsync(null);
        _main.Gallery.Albums.Typed = album;
        await _main.Gallery.Albums.ChooseCommand.ExecuteAsync(
            _main.Gallery.Albums.Choices.Single(choice => choice.Summary.Name == album));
    }

    /// <summary>
    /// A real JPEG whose header carries an orientation tag and room to change
    /// it, so turning it tells the file itself.
    /// </summary>
    private static void WriteJpegWithOrientation(string path)
    {
        byte[] pixels = new byte[80 * 60 * 3];
        BitmapSource picture = BitmapSource.Create(
            80, 60, 96, 96, PixelFormats.Rgb24, null, pixels, 80 * 3);

        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/PaddingSchema:Padding", (uint)2048);
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)1);

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(picture, null, metadata, null));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Files the open photograph through the viewer's own list.</summary>
    private async Task FileOpenPhotoAsync(string album)
    {
        await _main.Gallery.AddToAlbumCommand.ExecuteAsync(null);
        _main.Gallery.Albums.Typed = album;
        await _main.Gallery.Albums.CreateCommand.ExecuteAsync(null);
    }

    /// <summary>Deletes the open photograph the way the window's Delete button does.</summary>
    private async Task DeleteOpenPhotoAsync()
    {
        int assetId = _main.Gallery.OpenTile!.Item.Id;
        PhotoRemovalResult result = await DeleteDirectlyAsync(assetId);
        await _main.Gallery.AfterOpenPhotoDeletedAsync(result);
    }

    private async Task<PhotoRemovalResult> DeleteDirectlyAsync(int assetId)
    {
        using IServiceScope scope = _services.CreateScope();
        PhotoRemovalResult result = await scope.ServiceProvider
            .GetRequiredService<RemovePhotoHandler>()
            .HandleAsync([assetId]);

        Assert.Equal(1, result.Deleted);
        return result;
    }

    /// <summary>Moves one photograph into an album by writing it, as another screen would.</summary>
    private async Task MoveDirectlyAsync(int assetId, int albumId)
    {
        using IServiceScope scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAlbumRepository>().AddAsync(albumId, [assetId]);
    }

    private async Task<AlbumSummary?> AlbumOfAsync(int assetId)
    {
        using IServiceScope scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAlbumRepository>().FindForAssetAsync(assetId);
    }

    /// <summary>
    /// Six photographs of 2020 and two copies of one picture from 2021, each with
    /// a real rendition; Bali holds the first three and Sentosa the fourth; Ana
    /// is named in the first three, and the third has a second face nobody has
    /// named.
    /// </summary>
    private void Seed()
    {
        IThumbnailStore store = _services.GetRequiredService<IThumbnailStore>();

        using (IServiceScope scope = _services.CreateScope())
        {
            GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
            db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });

            for (int id = 1; id <= Photographs; id++)
            {
                db.Assets.Add(Photo(id, $@"2020\{id}.jpg", new DateTime(2020, 3, 1).AddDays(id), Rendition(store, id)));
            }

            string shared = Rendition(store, FirstCopy);
            db.Assets.Add(Photo(FirstCopy, @"copies\a.jpg", new DateTime(2021, 5, 1), shared));
            db.Assets.Add(Photo(SecondCopy, @"copies\b.jpg", new DateTime(2021, 5, 2), shared));

            var ana = new Person { DisplayName = "Ana" };
            db.Set<Person>().Add(ana);
            db.SaveChanges();

            foreach (int assetId in new[] { 1, 2, 3 })
            {
                var face = new Face
                {
                    AssetId = assetId,
                    Bounds = new FaceBounds(4, 4, 12, 12),
                    DetectScore = 0.9f,
                    Embedding = TestEmbeddings.At(0),
                };
                db.Faces.Add(face);
                db.SaveChanges();

                db.Set<FaceAssignment>().Add(new FaceAssignment
                {
                    FaceId = face.Id,
                    PersonId = ana.Id,
                    Source = AssignmentSource.Confirmed,
                    DecidedUtc = new DateTime(2020, 6, 1),
                });
            }

            db.Faces.Add(new Face
            {
                AssetId = 3,
                Bounds = new FaceBounds(40, 30, 12, 12),
                DetectScore = 0.9f,
                Embedding = TestEmbeddings.At(120),
            });

            db.SaveChanges();
        }

        using (IServiceScope scope = _services.CreateScope())
        {
            IAlbumRepository albums = scope.ServiceProvider.GetRequiredService<IAlbumRepository>();
            _bali = albums.CreateAsync("Bali").GetAwaiter().GetResult();
            albums.AddAsync(_bali, [1, 2, 3]).GetAwaiter().GetResult();
            _sentosa = albums.CreateAsync("Sentosa").GetAwaiter().GetResult();
            albums.AddAsync(_sentosa, [4]).GetAwaiter().GetResult();
        }
    }

    private static Asset Photo(int id, string relativePath, DateTime taken, string thumbnail) => new()
    {
        Id = id,
        PhotoSourceId = 1,
        RelativePath = relativePath,
        Length = 1,
        ModifiedUtc = taken,
        CreatedUtc = taken,
        IndexedUtc = taken,
        TakenUtc = taken,
        Kind = AssetKind.Photo,
        Status = AssetStatus.Ready,
        ThumbnailName = thumbnail,
        Width = 80,
        Height = 60,
    };

    /// <summary>A real tile and preview for one picture, named the way a scan names them.</summary>
    private static string Rendition(IThumbnailStore store, int seed) =>
        store.SaveAsync(new GeneratedThumbnail(
            TestJpeg.Bytes(40, 30),
            TestJpeg.Bytes(80, 60),
            80,
            60,
            null,
            new PerceptualHash((ulong)seed),
            seed.ToString("x32"))).GetAwaiter().GetResult();

    private sealed class SilentLog : IActivityLog
    {
        public void Append(string line)
        {
        }
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
            // A temporary folder; SQLite's pooled handle may outlive the provider.
        }
    }
}

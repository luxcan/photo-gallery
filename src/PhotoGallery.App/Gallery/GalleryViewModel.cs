using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Imaging;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Application.UseCases.Search;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;

namespace PhotoGallery.App.Gallery;

/// <summary>
/// The Library view: every picture newest first, a folder tree beside it, and
/// one picture at a time when you click into it.
/// </summary>
public sealed partial class GalleryViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThumbnailStore _thumbnails;

    /// <summary>
    /// The tiles, their rows, and the bound on how many are decoded at once.
    /// Shared with the People screen, which shows the same kind of grid.
    /// </summary>
    private readonly TileWindow _window;

    [ObservableProperty]
    private int _totalCount;

    /// <summary>
    /// The edge of one cell, which the user zooms with Ctrl and the wheel. Bound
    /// by the XAML rather than read from a constant, so it can move at runtime.
    /// </summary>
    [ObservableProperty]
    private double _cellSize = GalleryLayout.DefaultCellSize;

    /// <summary>
    /// Bound to the sort control. A bool rather than the enum because the two
    /// segments are radio buttons, and <c>IsChecked</c> takes a bool either way.
    /// </summary>
    [ObservableProperty]
    private bool _oldestFirst;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderViewVisible))]
    private bool _showFolders;

    [ObservableProperty]
    private FolderNode? _selectedFolder;

    /// <summary>Whose pictures the grid is restricted to, if anyone's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPersonFiltered), nameof(EmptyMessage))]
    private int? _personId;

    [ObservableProperty]
    private string _personName = string.Empty;

    /// <summary>Where the grid is restricted to, if anywhere.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaceFiltered), nameof(EmptyMessage))]
    private PlaceFilter? _place;

    [ObservableProperty]
    private string _placeName = string.Empty;

    /// <summary>What has been typed into the search box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchOpen;

    /// <summary>
    /// True while the box is being filled in by the app rather than by the user,
    /// so that showing the name of the person just chosen does not read as a new
    /// search and reopen the list underneath it.
    /// </summary>
    private bool _fillingSearch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewerOpen), nameof(OpenPhotoWhen), nameof(CanOfferPlay))]
    [NotifyCanExecuteChangedFor(nameof(NextPhotoCommand), nameof(PreviousPhotoCommand))]
    private GalleryTile? _openTile;

    [ObservableProperty]
    private ImageSource? _openPicture;

    /// <summary>
    /// The facts about the open photograph, in the panel every screen shares.
    /// </summary>
    /// <remarks>
    /// Read when a picture is opened rather than carried on every row. The grid
    /// holds eleven thousand items and draws fixed squares, so the size, the
    /// resolution and the digest would be three fields per row for a panel that
    /// shows one at a time.
    /// </remarks>
    [ObservableProperty]
    private PhotoDetails? _openDetails;

    [ObservableProperty]
    private bool _isLoading;

    public GalleryViewModel(IServiceScopeFactory scopeFactory, IThumbnailStore thumbnails)
    {
        _scopeFactory = scopeFactory;
        _thumbnails = thumbnails;
        _window = new TileWindow(thumbnails);

        Picker = new PersonPicker(
            AssignOpenFaceAsync,
            IgnoreFaceAsync,
            "Nobody — stop asking",
            closed: () => FacingBeingNamed = null);
    }

    /// <summary>
    /// Raised when this screen has changed what the library holds, so the counts
    /// in the status bar are stale.
    /// </summary>
    /// <remarks>
    /// The shell owns those counts and this screen cannot reach it, so it says
    /// what happened rather than going to fetch them - the same direction every
    /// other exchange between the two runs in.
    /// </remarks>
    public event EventHandler? LibraryChanged;

    /// <summary>
    /// Raised when a turn was refused because the photograph's folder is away,
    /// carrying the folders that could not be reached.
    /// </summary>
    /// <remarks>
    /// A badge was not enough. Being refused is something that just happened to
    /// the user in response to their own click, and it deserves the same modal
    /// answer deleting gives - the shell owns the dialog, so this says what
    /// happened and lets it do the asking.
    /// </remarks>
    public event EventHandler<IReadOnlyList<string>>? TurnRefusedOutOfReach;

    public ObservableCollection<GalleryRow> Rows => _window.Rows;

    public ObservableCollection<FolderNode> Folders { get; } = [];

    public bool IsFolderViewVisible => ShowFolders;

    public bool IsViewerOpen => OpenTile is not null;

    public bool IsEmpty => _window.Count == 0;

    /// <summary>
    /// Says what is on screen and, plainly, what is still missing - a grid of
    /// grey squares should never be a mystery.
    /// </summary>
    public string CountSummary
    {
        get
        {
            if (_window.Count == 0)
            {
                return string.Empty;
            }

            int waiting = _window.Tiles.Count(t => !t.IsPrepared);

            // Whatever is filtering is named, so a smaller count is always
            // explained rather than looking like pictures went missing. A person
            // and a place together are named together - "12 of Ana Lim in
            // Sentosa" - because either one alone would misdescribe the grid.
            string counted = (PersonId, Place) switch
            {
                (not null, not null) => $"{_window.Count:N0} of {PersonName} in {PlaceName}",
                (not null, null) => $"{_window.Count:N0} of {PersonName}",
                (null, not null) => $"{_window.Count:N0} in {PlaceName}",
                _ => SelectedFolder is null
                    ? $"{_window.Count:N0} pictures"
                    : $"{_window.Count:N0} in {SelectedFolder.Name}",
            };

            return waiting == 0
                ? counted
                : $"{counted} — {waiting:N0} not prepared yet";
        }
    }

    public string EmptyMessage => (PersonId, Place) switch
    {
        (not null, not null) => $"No pictures of {PersonName} in {PlaceName}.",
        (not null, null) => $"No pictures of {PersonName} yet. Confirm some faces under People.",
        (null, not null) => $"No pictures in {PlaceName}.",
        _ => SelectedFolder is null
            ? "No pictures yet. Add a folder under Photo sources, then scan it."
            : "Nothing in this folder.",
    };

    public bool IsPersonFiltered => PersonId is not null;

    public bool IsPlaceFiltered => Place is not null;

    public bool HasSearchText => SearchText.Length > 0;

    /// <summary>People and places whose name matches what is being typed.</summary>
    public ObservableCollection<SearchSuggestion> SearchMatches { get; } = [];

    public bool HasNoMatches => SearchMatches.Count == 0;

    /// <summary>The faces found in the photograph currently open.</summary>
    public ObservableCollection<PhotoFaceItem> OpenFaces { get; } = [];

    /// <summary>Everyone who has been named, before the current face marks one.</summary>
    private readonly List<PersonDirectoryEntry> _everyone = [];

    /// <summary>
    /// Whether boxes are drawn over the faces in the open picture.
    /// </summary>
    /// <remarks>
    /// Off until asked for. Looking at a photograph and labelling the people in
    /// it are different things to be doing, and boxes over every face are in the
    /// way of the first.
    /// </remarks>
    [ObservableProperty]
    private bool _showFaceNames;

    /// <summary>Which face the name list is currently asking about.</summary>
    [ObservableProperty]
    private PhotoFaceItem? _facingBeingNamed;

    /// <summary>
    /// The name list shown over the picture. The same one the review screen uses
    /// to correct a wrong guess - it is the same question in both places.
    /// </summary>
    public PersonPicker Picker { get; }

    /// <summary>
    /// The year the picture was taken, which is the year any name given here
    /// teaches.
    /// </summary>
    /// <remarks>
    /// Shown rather than asked for. Every photograph already carries its own
    /// date, so a name given on this one is automatically a statement about that
    /// person at that time - there is nothing for the user to choose and nothing
    /// they could get wrong.
    ///
    /// <para>Never the file's creation time: on a library assembled by copying
    /// that is the day it was copied, and 3,000 photographs spanning eight years
    /// carry thirteen distinct creation dates.</para>
    /// </remarks>
    public string OpenPhotoWhen => OpenTile is null
        ? string.Empty
        : OpenTile.Item.SortedOn.Year.ToString();

    public int OpenFaceCount => OpenFaces.Count;

    public string FaceSummary => OpenFaces.Count switch
    {
        0 => "No faces were found in this one.",
        1 => "1 face — click it to say who it is.",
        _ => $"{OpenFaces.Count} faces — click one to say who it is.",
    };

    /// <summary>
    /// Moving to another picture reloads its faces, so walking a folder with
    /// naming switched on stays switched on.
    /// </summary>
    /// <summary>
    /// What the badge beside the turn buttons says, or null for no badge.
    /// </summary>
    /// <remarks>
    /// Set from the row when a picture is opened, and again after a turn. Text
    /// rather than a flag because there are two different things to say and
    /// saying the wrong one is worse than saying nothing: a file that cannot
    /// hold an orientation tag is a permanent limitation of that file, while a
    /// source that is away is a temporary fact about the network. The badge used
    /// to report the first whenever it meant the second.
    /// </remarks>
    [ObservableProperty]
    private string? _turnNotice;

    /// <summary>The longer form of <see cref="TurnNotice"/>, shown on hover.</summary>
    [ObservableProperty]
    private string? _turnNoticeTip;

    /// <summary>
    /// The clip being played here, or null while the still is showing.
    /// </summary>
    /// <remarks>
    /// [08] put playback out of scope, and this is the line that moved: asked
    /// for twice, and the reason it was out of scope - that it would mean
    /// bundling a decoder - turns out not to hold. A poster already comes from
    /// whatever the machine can decode, and the same codecs answer here, so this
    /// bundles nothing and can fail honestly on the containers Windows does not
    /// know.
    ///
    /// <para>A path rather than a flag, because the element needs a source and
    /// because "which clip" is the thing that has to be cleared when the viewer
    /// moves on. Leaving it set would have a video carry on playing underneath
    /// the next photograph.</para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayingSource))]
    private string? _playingPath;

    /// <summary>
    /// Whether the clip is on screen in place of its still.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PlayingPath"/> on purpose, and the separation is
    /// what makes watching something twice free. Stopping used to clear the
    /// path, which cleared the player's source, which closed the file - so
    /// pressing play again fetched the whole thing back over the share. Now
    /// stopping only puts the still back; the clip stays loaded, and playing it
    /// again is a seek to zero.
    ///
    /// <para>The path is still cleared when the viewer moves to another picture,
    /// because holding a file open on somebody's network share while they browse
    /// their library is not a cache, it is a leak.</para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOfferPlay), nameof(CanScrub))]
    private bool _isPlayingVideo;

    /// <summary>
    /// The same clip as a URI, which is what the player element takes.
    /// </summary>
    /// <remarks>
    /// Built here rather than left to XAML's string-to-Uri conversion, because
    /// these paths are UNC and hold spaces - <c>\\nas\PhotoGallery\
    /// 20250419 - Kidzania\...</c> - and a conversion that guessed wrong would
    /// fail as a codec problem rather than as the path problem it is.
    /// </remarks>
    public Uri? PlayingSource =>
        PlayingPath is string path && Uri.TryCreate(path, UriKind.Absolute, out Uri? uri)
            ? uri
            : null;

    /// <summary>
    /// A quarter turn to draw the playing clip with, clockwise.
    /// </summary>
    /// <remarks>
    /// A phone records portrait video as a landscape stream plus a flag saying
    /// which way up it goes. The Windows shell honours that flag, so the poster
    /// is upright; <c>MediaElement</c> does not, so the clip plays on its side -
    /// upright still, sideways film, from the same file.
    ///
    /// <para>Worked out by comparing the two rather than by reading the
    /// container: the poster is known to be the right way up, so a poster that
    /// is portrait while the stream is landscape is exactly the case that needs
    /// turning. That needs no parser and no new dependency, and it is silent on
    /// the clips that are already correct.</para>
    /// </remarks>
    [ObservableProperty]
    private double _playingRotation;

    /// <summary>
    /// Says how the stream is shaped against the still, so the clip can be drawn
    /// the way the picture already is.
    /// </summary>
    /// <param name="streamIsPortrait">Whether the decoded stream is taller than wide.</param>
    /// <param name="stillIsPortrait">Whether the poster is.</param>
    public void AlignPlaybackTo(bool streamIsPortrait, bool stillIsPortrait)
    {
        // Ninety rather than two-seventy: a phone held upright writes a
        // ninety-degree flag, which is nearly all of this library's portrait
        // video. A clip that comes out upside down is one the user can turn.
        PlayingRotation = streamIsPortrait == stillIsPortrait ? 0d : 90d;
    }

    /// <summary>How far into the clip the picture on screen is, in seconds.</summary>
    /// <remarks>
    /// Seconds rather than a <see cref="TimeSpan"/> because a slider binds to a
    /// number, and this is the one place the two have to meet. Written from a
    /// timer while the clip runs, and by the user while they drag - which is why
    /// <see cref="IsScrubbing"/> exists to stop the two fighting.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionCaption))]
    private double _playbackSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(PositionCaption), nameof(HasPlaybackLength), nameof(CanScrub))]
    private double _playbackLengthSeconds;

    /// <summary>
    /// True while the user has hold of the slider.
    /// </summary>
    /// <remarks>
    /// The timer must not write the position back underneath a drag, or the
    /// thumb springs out from under the finger every quarter second.
    /// </remarks>
    public bool IsScrubbing { get; set; }

    /// <summary>
    /// One viewer write at a time.
    /// </summary>
    /// <remarks>
    /// Naming a face now hands the screen back before its write has finished, so
    /// the turn and delete buttons are reachable while it is still running - and
    /// all three rewrite the same face rows. SQLite takes one writer, and the
    /// second gets a locked database rather than a queue, so the queue is here.
    /// </remarks>
    private readonly SemaphoreSlim _viewerWrites = new(1, 1);

    /// <summary>
    /// Whether the length is known well enough to offer a bar to drag.
    /// </summary>
    /// <remarks>
    /// A container that will not say how long it is can still be played; it just
    /// cannot be scrubbed, and a slider with no end would be a lie about what
    /// dragging it does.
    /// </remarks>
    public bool HasPlaybackLength => PlaybackLengthSeconds > 0d;

    /// <summary>Whether the bar is worth showing: something is playing and it has an end.</summary>
    public bool CanScrub => IsPlayingVideo && HasPlaybackLength;

    /// <summary>Where we are and how far there is to go, as a person reads it.</summary>
    public string PositionCaption =>
        $"{Clock(PlaybackSeconds)} / {Clock(PlaybackLengthSeconds)}";

    private static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(double.IsFinite(seconds) && seconds > 0 ? seconds : 0);
        return span >= TimeSpan.FromHours(1) ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    /// <summary>Turns the playing clip a quarter, clockwise or back.</summary>
    /// <remarks>
    /// Only while it plays, and not written down. The turn a video needs belongs
    /// to how it was recorded rather than to what the user thinks of it, and the
    /// automatic answer above is right for nearly all of them - this is for the
    /// handful it is not, and asking again next time costs one click.
    /// </remarks>
    public void TurnPlayingVideo(int degrees) =>
        PlayingRotation = ((PlayingRotation + degrees) % 360d + 360d) % 360d;

    /// <summary>
    /// Whether the badge inviting a play should be over the still.
    /// </summary>
    /// <remarks>
    /// Not while it is already playing, and not once this machine has said it
    /// cannot: a badge that has just failed is an invitation to press it again
    /// and get the same nothing.
    /// </remarks>
    public bool CanOfferPlay =>
        OpenTile?.IsVideo == true && !IsPlayingVideo && PlaybackError is null;

    /// <summary>
    /// Said when the machine has no codec for this container - the one case
    /// where playing here cannot work and something else has to be offered.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOfferPlay), nameof(HasPlaybackError))]
    private string? _playbackError;

    public bool HasPlaybackError => PlaybackError is not null;

    /// <summary>Starts the open clip playing in place.</summary>
    [RelayCommand]
    private void PlayVideo()
    {
        if (OpenTile is not GalleryTile tile || !tile.IsVideo)
        {
            return;
        }

        PlaybackError = null;

        // Said rather than silently skipped. A path this app cannot resolve, or
        // a file that is not where the index says, used to leave the badge
        // sitting there doing nothing on every click - which is indistinguishable
        // from a broken button, and is exactly the moment the user asked to be
        // told the video is not available.
        string path = tile.Item.FullPath;
        if (path.Length == 0)
        {
            PlaybackError = "This video is not available - Photo Gallery no longer knows "
                            + "which folder it came from.";
            return;
        }

        if (!File.Exists(path))
        {
            PlaybackError = "This video is not available - the folder it lives in cannot be "
                            + "reached. Reconnect and try again.";
            return;
        }

        // Setting the same path again would close the file and fetch it over the
        // share a second time for a clip the player already has - which is both
        // the second watch of one clip and, now, every first watch, because
        // opening the picture warmed it.
        if (!string.Equals(PlayingPath, path, StringComparison.OrdinalIgnoreCase))
        {
            PlayingPath = path;
        }

        IsPlayingVideo = true;
    }

    /// <summary>
    /// Gives the player the clip as soon as its picture is opened, without
    /// starting it.
    /// </summary>
    /// <remarks>
    /// Nothing plays: the element is Manual, so handing it a source opens the
    /// file and buffers the front of it and stops there. What that buys is the
    /// wait - the header and first read over a share are seconds, and they are
    /// spent while somebody is looking at the poster rather than after they have
    /// asked to watch.
    ///
    /// <para>Silent about a file it cannot find, unlike pressing play. Somebody
    /// who has not asked to watch anything should not be told that they cannot.</para>
    /// </remarks>
    private void WarmPlayer(GalleryTile? tile)
    {
        // Whatever was being pulled in belongs to the picture being left.
        _warming?.Cancel();
        _warming?.Dispose();
        _warming = null;

        if (tile?.IsVideo != true)
        {
            return;
        }

        string path = tile.Item.FullPath;

        try
        {
            if (path.Length == 0 || !File.Exists(path))
            {
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A share that cannot be asked is one this simply does not warm.
            DiagnosticLog.Write($"could not reach {path} to warm the player", ex);
            return;
        }

        // The source too, so the element has it the moment it is shown - though
        // the reading below is what actually buys the time, because a player
        // that is not on screen does not open its file.
        PlayingPath = path;

        _warming = new CancellationTokenSource();
        _ = VideoPrefetch.WarmAsync(path, _warming.Token);
    }

    /// <summary>Cancels the read-ahead when the viewer moves on.</summary>
    private CancellationTokenSource? _warming;

    /// <summary>Puts the still back, and stops the sound with it.</summary>
    [RelayCommand]
    public void StopVideo()
    {
        // The still comes back, and the clip stays loaded. See IsPlayingVideo:
        // this is what makes watching it again cost nothing.
        IsPlayingVideo = false;
        PlaybackError = null;
        PlaybackSeconds = 0d;
    }

    /// <summary>
    /// Records that this container will not play here, so the viewer can offer
    /// the player Windows uses instead of leaving a black rectangle.
    /// </summary>
    public void ReportPlaybackFailed()
    {
        // Cleared here, unlike stopping: keeping a source the player has just
        // refused would only make the next press fail again from memory.
        PlayingPath = null;
        IsPlayingVideo = false;
        PlaybackLengthSeconds = 0d;
        PlaybackError = "This video cannot be played here - Windows has no codec for it "
                        + "on this computer.";
    }

    partial void OnOpenTileChanged(GalleryTile? value)
    {
        // Before anything else: whatever was playing belongs to the picture
        // being left, and its sound must not follow the user to the next one.
        // The path goes too, not just the picture: this is the moment the file
        // on the share is released.
        PlayingPath = null;
        IsPlayingVideo = false;
        PlaybackError = null;
        PlayingRotation = 0d;
        PlaybackSeconds = 0d;
        PlaybackLengthSeconds = 0d;

        // Then handed straight back for a video, which is the whole of this
        // optimisation: the player opens the file and reads its header and first
        // buffer while the poster is on screen and nobody is waiting. Measured
        // over the share, that is the 2.6 seconds that used to sit between
        // pressing play and seeing anything.
        //
        // It costs a read for a video that is opened and never played. That is
        // the right trade: opening one is already a deliberate act, where the
        // grid opens none of them.
        WarmPlayer(value);

        TurnNotice = value?.Item.IsTurnedInAppOnly == true ? TurnNotices.HereOnly : null;
        TurnNoticeTip = value?.Item.IsTurnedInAppOnly == true ? TurnNotices.HereOnlyTip : null;

        OpenDetails = null;
        if (value is not null)
        {
            _ = LoadOpenDetailsAsync(value.Item.Id);
        }

        if (!ShowFaceNames)
        {
            return;
        }

        if (value is null)
        {
            OpenFaces.Clear();
            FacingBeingNamed = null;
            return;
        }

        _ = LoadOpenFacesAsync();
    }

    /// <summary>
    /// Reads what is known about the open photograph for the detail panel.
    /// </summary>
    /// <remarks>
    /// Only the picture still open may fill it in. Holding an arrow key down
    /// starts one of these per press, and without the check whichever finished
    /// last would win - putting one photograph's numbers beside another's.
    /// </remarks>
    private async Task LoadOpenDetailsAsync(int assetId)
    {
        PhotoFacts? facts;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            facts = await scope.ServiceProvider
                .GetRequiredService<IAssetRepository>()
                .FindFactsAsync(assetId)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            DiagnosticLog.Write($"could not read the details of asset {assetId}", ex);
            return;
        }

        if (facts is not null && OpenTile?.Item.Id == assetId)
        {
            OpenDetails = PhotoDetails.Of(facts);
        }
    }

    /// <summary>
    /// Places the boxes again once the picture behind them has arrived.
    /// </summary>
    /// <remarks>
    /// The faces are read and the preview decoded independently, so either can
    /// finish first. Placing a box needs the picture's pixel size, so whichever
    /// lands last has to do the arithmetic - without this, opening a photograph
    /// from the grid could lay the boxes out against the one before it.
    /// </remarks>
    partial void OnOpenPictureChanged(ImageSource? value) =>
        LayoutFaces(_faceAreaWidth, _faceAreaHeight);

    partial void OnShowFaceNamesChanged(bool value)
    {
        if (value)
        {
            _ = LoadOpenFacesAsync();
        }
        else
        {
            OpenFaces.Clear();
            FacingBeingNamed = null;
            OnPropertyChanged(nameof(OpenFaceCount));
            OnPropertyChanged(nameof(FaceSummary));
        }
    }

    /// <summary>Reads the faces in the open picture and who they are said to be.</summary>
    private async Task LoadOpenFacesAsync()
    {
        OpenFaces.Clear();
        FacingBeingNamed = null;

        if (OpenTile is null)
        {
            return;
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IPeopleReader reader = scope.ServiceProvider.GetRequiredService<IPeopleReader>();

            foreach (FaceOnPhoto face in await reader.GetFacesOnAsync(OpenTile.Item.Id)
                         .ConfigureAwait(true))
            {
                OpenFaces.Add(new PhotoFaceItem(face));
            }

            // Everyone, not the search box's shortlist. Searching narrows a list
            // as you type and can fairly stop at the best few; being asked who
            // somebody is and offered eight of your eleven names is a dead end,
            // because the one you want may simply not be there.
            _everyone.Clear();
            _everyone.AddRange((await reader.GetDirectoryAsync().ConfigureAwait(true))
                .OrderByDescending(person => person.Photos)
                .ThenBy(person => person.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // A picture whose faces cannot be read still shows the picture.
            OpenFaces.Clear();
        }

        OnPropertyChanged(nameof(OpenFaceCount));
        OnPropertyChanged(nameof(FaceSummary));
        LayoutFaces(_faceAreaWidth, _faceAreaHeight);
    }

    private double _faceAreaWidth;
    private double _faceAreaHeight;

    /// <summary>
    /// Places every box for a picture drawn into an area of this size.
    /// </summary>
    /// <remarks>
    /// Called whenever the area changes as well as when the faces load, because
    /// a maximised window and a restored one draw the same picture at very
    /// different sizes and a box that did not follow would sit over nothing.
    /// </remarks>
    public void LayoutFaces(double areaWidth, double areaHeight)
    {
        _faceAreaWidth = areaWidth;
        _faceAreaHeight = areaHeight;

        if (OpenFaces.Count == 0
            || PictureFit.Of(areaWidth, areaHeight, OpenPicture) is not PictureFit fit)
        {
            return;
        }

        foreach (PhotoFaceItem face in OpenFaces)
        {
            face.PlaceWithin(fit);
        }
    }

    /// <summary>
    /// Opens the name list on one face, marking the name already on it.
    /// </summary>
    /// <remarks>
    /// Clicking a face that is already someone should say so, not present a
    /// blank list and leave the user to remember what they are changing.
    /// </remarks>
    [RelayCommand]
    private void BeginNamingFace(PhotoFaceItem? face)
    {
        FacingBeingNamed = face;

        if (face is not null)
        {
            Picker.Open(
                _everyone,
                face.Face.PersonId,
                "Who is this?",
                $"This teaches the app what they looked like in {OpenPhotoWhen}.");
        }
    }

    private void CancelNamingFace()
    {
        FacingBeingNamed = null;
        Picker.Close();
    }

    /// <summary>
    /// Sets this face aside as nobody worth tracking.
    /// </summary>
    /// <remarks>
    /// Strangers in the background outnumber the people a library is about, and
    /// refusing them one person at a time never ends. Reversible: naming the
    /// face later brings it straight back.
    /// </remarks>
    private async Task IgnoreFaceAsync()
    {
        if (FacingBeingNamed is not PhotoFaceItem face)
        {
            return;
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IPeopleRepository>()
                .SetIgnoredAsync([face.FaceId], ignored: true)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            CancelNamingFace();
            return;
        }

        CancelNamingFace();
        await LoadOpenFacesAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Says who one face is, and lets that teach the rest of the library.
    /// </summary>
    /// <remarks>
    /// A confirmation, not a proposal: the user is looking straight at the
    /// picture, which is a better source than anything the app can work out.
    /// </remarks>
    private async Task AssignOpenFaceAsync(string displayName)
    {
        if (FacingBeingNamed is not PhotoFaceItem face)
        {
            return;
        }

        // The screen first, the database after. Naming a face is not a small
        // write - it confirms this face and then offers the same person to
        // everything else that looks like them across the library - and holding
        // the picker open until that returns made a click that is answering an
        // obvious question feel like it had not registered.
        //
        // Nothing here is guessed: the user has just said who this is, and the
        // box shows exactly what they said. The only thing still in doubt is
        // whether it reached the disk, which is what the failure path below is
        // for.
        int at = OpenFaces.IndexOf(face);
        FaceOnPhoto named = face.Face with
        {
            PersonName = displayName,
            Source = AssignmentSource.Confirmed,
            IsIgnored = false,
        };

        CancelNamingFace();

        if (at >= 0)
        {
            OpenFaces[at] = new PhotoFaceItem(named);
        }

        try
        {
            // Off the dispatcher, and one at a time. Off, because confirming a
            // face re-proposes that person across the whole library and doing
            // that on the UI thread is the freeze this change exists to remove.
            // One at a time, because the turn and delete buttons write the same
            // face rows - and the user can reach them the moment this returns
            // control to the screen. Two writers on one SQLite file is a locked
            // database, or boxes moved by one and rewritten by the other.
            await _viewerWrites.WaitAsync().ConfigureAwait(true);
            try
            {
                await Task.Run(async () =>
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider
                        .GetRequiredService<AssignFacesHandler>()
                        .HandleAsync(new AssignFacesRequest(
                            [face.FaceId], AssignmentSource.Confirmed, DisplayName: displayName))
                        .ConfigureAwait(false);
                }).ConfigureAwait(true);
            }
            finally
            {
                _viewerWrites.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            DiagnosticLog.Write($"could not record {displayName} on face {face.FaceId}", ex);

            // Put the question back. A name left on screen that never reached
            // the disk is worse than the lag this replaced: the user would think
            // the job was done.
            if (at >= 0 && at < OpenFaces.Count)
            {
                OpenFaces[at] = new PhotoFaceItem(face.Face);
            }

            return;
        }

        // Naming a face here can create somebody who did not exist a moment ago,
        // which the status bar's people count is showing.
        LibraryChanged?.Invoke(this, EventArgs.Empty);

        // Re-read once the write is done, because confirming one face can change
        // what is proposed on the others in this same picture. The boxes are
        // already right about the one that was just named, so this settles the
        // rest without anybody waiting on it.
        await LoadOpenFacesAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Why the list is empty, which is never the same reason twice.
    /// </summary>
    /// <remarks>
    /// The dropdown offers only the two things it can match exactly - a person
    /// and a place - but the box answers a third, and pressing Enter hands
    /// anything left over to the description search. So an empty list is not a
    /// dead end, and saying only "nobody named that" told the user the search had
    /// failed when it had not started.
    ///
    /// <para>Nothing named at all is a different problem again, and it is the one
    /// with something the user can do about it.</para>
    /// </remarks>
    public string NoMatchMessage
    {
        get
        {
            if (!HasSearchText)
            {
                return "No names or places yet. Name faces under People, or scan "
                       + "your folders to work out where the photos were taken.";
            }

            return $"No one and nowhere called “{SearchText.Trim()}”. "
                   + "Press Enter to look for pictures of it instead.";
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_fillingSearch)
        {
            _ = SearchAsync();
        }
    }

    /// <summary>
    /// Offers the names that match, and drops a person filter as soon as the box
    /// is emptied.
    /// </summary>
    /// <remarks>
    /// Run on every keystroke rather than behind a delay. It is one small query
    /// over a handful of rows - a library has people, not millions of them - and
    /// a search box that lags behind what has been typed feels broken.
    /// </remarks>
    private async Task SearchAsync()
    {
        if (!HasSearchText && (IsPersonFiltered || IsPlaceFiltered))
        {
            await ShowEverythingAsync().ConfigureAwait(true);
        }

        IReadOnlyList<PersonDirectoryEntry> people;
        IReadOnlyList<PlaceDirectoryEntry> places;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            people = await scope.ServiceProvider
                .GetRequiredService<FindPeopleHandler>()
                .HandleAsync(SearchText)
                .ConfigureAwait(true);

            places = await scope.ServiceProvider
                .GetRequiredService<FindPlacesHandler>()
                .HandleAsync(SearchText)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // A search that cannot be answered offers nothing rather than
            // interrupting whatever the user is doing.
            SearchMatches.Clear();
            IsSearchOpen = false;
            return;
        }

        SearchMatches.Clear();
        foreach (SearchSuggestion match in Merge(
                     [.. people.Select(SearchSuggestion.ForPerson)],
                     [.. places.Select(SearchSuggestion.ForPlace)]))
        {
            SearchMatches.Add(match);
        }

        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(NoMatchMessage));

        // Opened even with nothing to offer, so that a name nobody has been
        // given says so rather than looking like a box that does not work.
        IsSearchOpen = true;
    }

    /// <summary>
    /// One list from two, with people first and neither able to crowd the other
    /// out.
    /// </summary>
    /// <remarks>
    /// Each kind is guaranteed half the rows before either takes what is left,
    /// so a library with eight matching names cannot hide every matching place -
    /// and typing a word that is both still shows both. People lead for the same
    /// reason they win a tie in the split: their names were written down
    /// deliberately, where places came out of a gazetteer nobody chose.
    /// </remarks>
    private static IEnumerable<SearchSuggestion> Merge(
        IReadOnlyList<SearchSuggestion> people, IReadOnlyList<SearchSuggestion> places)
    {
        const int most = FindPeopleHandler.MaxMatches;
        int share = most / 2;

        int forPeople = Math.Min(people.Count, Math.Max(share, most - places.Count));
        int forPlaces = Math.Min(places.Count, most - forPeople);

        return people.Take(forPeople).Concat(places.Take(forPlaces));
    }

    /// <summary>Offers every name, which is what clicking into an empty box should do.</summary>
    [RelayCommand]
    private Task OpenSearchAsync() => SearchAsync();

    [RelayCommand]
    private async Task ShowMatchAsync(SearchSuggestion? match)
    {
        if (match is null)
        {
            return;
        }

        if (match.Place is PlaceFilter where)
        {
            ShowPlace(where, match.DisplayName);
        }
        else
        {
            ShowPerson(match.PersonId, match.DisplayName);
        }

        IsSearchOpen = false;

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Enter answers the whole line: whoever is named in it, and whatever the
    /// rest of it describes.
    /// </summary>
    /// <remarks>
    /// Only on Enter, never on a keystroke. Offering names as they are typed is
    /// one small query over a handful of rows; answering a description runs a
    /// text encoder and compares against every picture in the library, and doing
    /// that per keystroke would make the box unusable.
    ///
    /// <para>A line naming somebody and nothing else keeps the behaviour it has
    /// always had, down to still going through the dropdown, so nothing about
    /// searching for a person changed when searching for a thing arrived.</para>
    /// </remarks>
    [RelayCommand]
    private async Task AcceptSearchAsync()
    {
        if (!HasSearchText)
        {
            return;
        }

        PhotoSearch search;

        // Busy from here rather than from the reload. The first description of
        // an app's life waits about two seconds for the text encoder to be read
        // off disk, and two seconds of nothing happening reads as a box that
        // does not work.
        IsLoading = true;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            search = await scope.ServiceProvider
                .GetRequiredService<SearchPhotosHandler>()
                .HandleAsync(SearchText)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // Most likely the search models are not installed. Falling back to
            // the name match keeps the box doing what it could always do.
            SearchNote = ex.Message;
            await FallBackToNameAsync().ConfigureAwait(true);
            return;
        }
        finally
        {
            // Cleared here because the reload below sets it again for itself. A
            // failure or an empty question has to put it down either way.
            IsLoading = false;
        }

        if (search.Terms.IsEmpty)
        {
            return;
        }

        // Nothing described yet. Said plainly, because it is the one empty
        // result the user can do something about.
        if (search.NothingIndexed)
        {
            SearchNote =
                "No pictures have been described yet. Run “Learn what the pictures are of” "
                + "to search by what is in them.";
            await FallBackToNameAsync().ConfigureAwait(true);
            return;
        }

        IsSearchOpen = false;
        SearchMatches.Clear();

        SelectedFolder = null;
        PersonId = search.Terms.PersonId;
        PersonName = search.Terms.PersonName ?? string.Empty;
        Place = search.Terms.Place;
        PlaceName = search.Terms.PlaceName ?? string.Empty;
        _ranked = search.Ranked;

        // What the app understood, so a wrong split is visible rather than
        // mysterious - "Ana Lim - beach" against a line that was typed as one.
        SearchNote = search.Terms.Describe();

        OnPropertyChanged(nameof(IsPersonFiltered));
        OnPropertyChanged(nameof(IsPlaceFiltered));
        OnPropertyChanged(nameof(IsContentFiltered));

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Answers with the best suggestion, which is what the box did before it
    /// could answer descriptions.
    /// </summary>
    private Task FallBackToNameAsync() =>
        SearchMatches.Count == 0 ? Task.CompletedTask : ShowMatchAsync(SearchMatches[0]);

    /// <summary>
    /// What the app made of the line that was typed, or why it could not answer
    /// it.
    /// </summary>
    [ObservableProperty]
    private string _searchNote = string.Empty;

    /// <summary>Whether a description is narrowing what is on screen.</summary>
    public bool IsContentFiltered => _ranked is not null;

    /// <summary>
    /// The photographs a description matched, best first.
    /// </summary>
    /// <remarks>
    /// Held rather than re-derived because it is the answer: re-running the
    /// search on every reload would spend a text encoder and a pass over the
    /// whole library to arrive at the list already in hand.
    /// </remarks>
    private IReadOnlyList<int>? _ranked;

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        IsSearchOpen = false;
        SearchMatches.Clear();

        if (PersonId is null && Place is null && !IsContentFiltered)
        {
            Fill(string.Empty);
            SearchNote = string.Empty;
            return;
        }

        Fill(string.Empty);

        if (PersonId is null && Place is null)
        {
            // A description naming nobody and nowhere: clearing takes the grid
            // back to the whole library, which neither ShowEveryone nor
            // ShowEverywhere would do, because there is no filter to drop.
            ClearRanking();
            await LoadAsync().ConfigureAwait(true);
            return;
        }

        await ShowEverythingAsync().ConfigureAwait(true);
    }

    /// <summary>Reads the library and lays it out.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            GalleryQuery query = CurrentQuery();

            GalleryPage page;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                page = await scope.ServiceProvider
                    .GetRequiredService<QueryGalleryHandler>()
                    .HandleAsync(query, cancellationToken);
            }

            // Filling also forgets the position asked for before: different
            // pictures under the same index say nothing about what is on screen
            // now.
            _window.Fill(page.Items.Select(item => new GalleryTile(item)));

            TotalCount = page.TotalCount;
            await _window.MarkPreparedAsync(cancellationToken);
            NotifyCounts();

            await ShowRangeAsync(0, cancellationToken);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// What the grid is currently asking for.
    /// </summary>
    /// <remarks>
    /// A selected node with no relative folder is the photo source itself, which
    /// means everything in that source rather than any one folder beneath it.
    /// </remarks>
    private GalleryQuery CurrentQuery() =>
        new(PhotoSourceId: SelectedFolder?.PhotoSourceId,
            FolderPath: string.IsNullOrEmpty(SelectedFolder?.RelativeFolder)
                ? null
                : SelectedFolder.RelativeFolder,
            // Videos join the photographs now that they carry a poster - and the
            // reader admits only the ones that do, so this does not put back the
            // grey squares that kept them out.
            IncludeVideos: true,
            SortOrder: SortOrder,
            PersonId: PersonId,
            RankedAssetIds: _ranked,
            Place: Place);

    /// <summary>
    /// Shows only the pictures one person is in.
    /// </summary>
    /// <remarks>
    /// The folder choice is cleared first. "Every photo of one person" means every
    /// photo, and leaving a folder selected would quietly answer a narrower
    /// question than the one that was asked.
    /// </remarks>
    public void ShowPerson(int personId, string displayName)
    {
        SelectedFolder = null;
        PersonId = personId;
        PersonName = displayName;

        // Choosing a name is a fresh question, so whatever a description or a
        // place had narrowed things to no longer applies. "Every photo of my
        // son" from the People screen must not quietly still mean "in Sentosa".
        ClearPlace();
        ClearRanking();

        // The box says what the grid is showing, however the filter was reached -
        // by typing a name or by pressing Show over in People.
        Fill(displayName);
    }

    /// <summary>
    /// Shows only the pictures taken in one place.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="ShowPerson"/>, down to clearing the folder: a
    /// place is a question about the whole library, and leaving a folder selected
    /// would answer a narrower one than was asked.
    /// </remarks>
    public void ShowPlace(PlaceFilter filter, string name)
    {
        SelectedFolder = null;
        Place = filter;
        PlaceName = name;

        PersonId = null;
        PersonName = string.Empty;
        OnPropertyChanged(nameof(IsPersonFiltered));

        ClearRanking();
        Fill(name);
    }

    [RelayCommand]
    private async Task ShowEveryoneAsync()
    {
        if (PersonId is null)
        {
            return;
        }

        PersonId = null;
        PersonName = string.Empty;
        ClearRanking();
        Fill(string.Empty);

        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ShowEverywhereAsync()
    {
        if (Place is null)
        {
            return;
        }

        ClearPlace();
        ClearRanking();
        Fill(string.Empty);

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Drops every filter at once, for an emptied search box.</summary>
    private async Task ShowEverythingAsync()
    {
        if (PersonId is null && Place is null)
        {
            return;
        }

        PersonId = null;
        PersonName = string.Empty;
        ClearPlace();
        ClearRanking();

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Forgets which place the grid was restricted to.</summary>
    private void ClearPlace()
    {
        Place = null;
        PlaceName = string.Empty;
    }

    /// <summary>Forgets what a description had narrowed the grid to.</summary>
    private void ClearRanking()
    {
        _ranked = null;
        SearchNote = string.Empty;
        OnPropertyChanged(nameof(IsContentFiltered));
    }

    /// <summary>
    /// Puts text in the search box without it counting as a search.
    /// </summary>
    private void Fill(string text)
    {
        _fillingSearch = true;
        try
        {
            SearchText = text;
        }
        finally
        {
            _fillingSearch = false;
        }
    }

    /// <summary>Which end of the library the grid starts at.</summary>
    public GallerySortOrder SortOrder =>
        OldestFirst ? GallerySortOrder.OldestFirst : GallerySortOrder.NewestFirst;

    /// <summary>
    /// Applies a stored order without re-reading the library, for use while a
    /// library is opening and the grid has not loaded at all.
    /// </summary>
    /// <remarks>
    /// Goes through the property so the control updates, but flags the change as
    /// restored rather than chosen. The handler reloads on a real choice, which
    /// is right when the user works the control and wasteful before there is
    /// anything to reload.
    /// </remarks>
    public void SetSortOrder(GallerySortOrder sortOrder)
    {
        _restoringSortOrder = true;
        try
        {
            OldestFirst = sortOrder == GallerySortOrder.OldestFirst;
        }
        finally
        {
            _restoringSortOrder = false;
        }
    }

    private bool _restoringSortOrder;

    public async Task LoadFoldersAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FolderNode> folders;
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            folders = await scope.ServiceProvider
                .GetRequiredService<GetFolderTreeHandler>()
                .HandleAsync(cancellationToken);
        }

        Folders.Clear();
        foreach (FolderNode folder in folders)
        {
            Folders.Add(folder);
        }
    }

    /// <summary>
    /// Tries again for the tiles that had nothing to show, so the grid fills in
    /// while a preparation pass is still running rather than waiting for it.
    /// </summary>
    public async Task RefreshMissingPicturesAsync(CancellationToken cancellationToken = default)
    {
        // A preparation pass reports every 25 pictures. Each refresh re-reads
        // every row and checks every rendition on disk - measured at 691 ms for
        // this library - so refreshing on each report kept the UI thread busy
        // continuously and the window stopped responding. One at a time, and no
        // more often than the interval below.
        if (_refreshing || DateTime.UtcNow - _lastRefresh < RefreshInterval)
        {
            return;
        }

        _refreshing = true;
        try
        {
            List<GalleryTile> waiting = _window.WaitingNearTheViewport();
            if (waiting.Count == 0)
            {
                return;
            }

            await AdoptNewNamesAsync(waiting, cancellationToken);
            await _window.LoadPicturesAsync(waiting, cancellationToken);
        }
        finally
        {
            _refreshing = false;
            _lastRefresh = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// How often the grid may re-read what has been prepared while a pass runs.
    /// Often enough to feel live, rarely enough to leave the UI thread alone.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private DateTime _lastRefresh = DateTime.MinValue;

    /// <summary>
    /// Re-reads which rendition each waiting picture now has.
    /// </summary>
    /// <remarks>
    /// Preparing a picture renames its rendition, because the name comes from
    /// the picture's content rather than from its row. Without this the grid
    /// would keep asking for the name it was loaded with and stay blank however
    /// long the pass ran.
    /// </remarks>
    private async Task AdoptNewNamesAsync(
        IReadOnlyList<GalleryTile> waiting, CancellationToken cancellationToken)
    {
        GalleryQuery query = CurrentQuery();

        GalleryPage current;
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            current = await scope.ServiceProvider
                .GetRequiredService<QueryGalleryHandler>()
                .HandleAsync(query, cancellationToken);
        }

        Dictionary<int, string?> names = current.Items.ToDictionary(
            item => item.Id, item => item.ThumbnailName);

        foreach (GalleryTile tile in _window.Tiles)
        {
            if (names.TryGetValue(tile.Item.Id, out string? name))
            {
                tile.ThumbnailName = name;
            }
        }

        await _window.MarkPreparedAsync(cancellationToken);
        NotifyCounts();
    }

    private bool _refreshing;

    /// <summary>
    /// Resizes the cells. Rows are not re-chunked here - that depends on how many
    /// now fit across, which only the view knows - so the caller follows this with
    /// <see cref="SetColumns"/>.
    /// </summary>
    public void SetCellSize(double cellSize)
    {
        CellSize = GalleryLayout.Normalise(cellSize);
    }

    public int Columns => _window.Columns;

    /// <summary>Tells the grid how tall the window is, in rows of pictures.</summary>
    public void SetVisibleRows(int rows) => _window.SetVisibleRows(rows);

    /// <summary>Re-chunks the rows for a new width.</summary>
    public void SetColumns(int columns) => _window.SetColumns(columns);

    /// <summary>
    /// Decodes the pictures around a position in the grid and releases the ones
    /// far from it.
    /// </summary>
    public Task ShowRangeAsync(
        int firstVisibleItem, CancellationToken cancellationToken = default) =>
        _window.ShowRangeAsync(firstVisibleItem, cancellationToken);

    /// <summary>
    /// The grid the open picture came out of, or null for the library's own.
    /// </summary>
    /// <remarks>
    /// The People screen shows its own grid of one person's pictures in this
    /// same viewer. Without this, next and previous walked the library instead -
    /// and since the open tile was not in it, they found nothing to walk at all.
    /// </remarks>
    private TileWindow? _viewerSource;

    /// <summary>What next and previous step through.</summary>
    private TileWindow ViewerGrid => _viewerSource ?? _window;

    [RelayCommand]
    private void OpenPhoto(GalleryTile? tile)
    {
        _viewerSource = null;
        Open(tile);
    }

    /// <summary>
    /// Opens a picture belonging to another screen's grid, so the viewer steps
    /// through that one.
    /// </summary>
    public void OpenFrom(TileWindow source, GalleryTile? tile)
    {
        ArgumentNullException.ThrowIfNull(source);

        _viewerSource = source;
        Open(tile);
    }

    private void Open(GalleryTile? tile)
    {
        if (tile is null)
        {
            return;
        }

        OpenTile = tile;
        OpenPicture = TileImageLoader.LoadPreview(_thumbnails, tile.ThumbnailName)
                   ?? tile.Picture;
    }

    /// <summary>
    /// Turns the open photograph a quarter turn, and redraws it.
    /// </summary>
    /// <remarks>
    /// For pictures whose file does not say which way up they belong - a phone
    /// held upside down that wrote no orientation tag. The cached copies are
    /// turned, the face boxes go with them so no confirmed name is lost, and the
    /// turn is recorded so a later preparation pass reapplies it.
    ///
    /// <para>The tile in the grid is refreshed too. It is the same picture, and
    /// leaving it upside down behind a viewer showing it the right way up is the
    /// kind of disagreement that reads as a bug.</para>
    /// </remarks>
    [RelayCommand]
    private async Task TurnPhotoAsync(string? degrees)
    {
        if (OpenTile is not GalleryTile tile
            || !int.TryParse(degrees, out int turn))
        {
            return;
        }

        // A clip on screen turns here and nowhere else. Turning its still
        // instead is what this looked like it was doing - the poster rotated
        // and the film carried on sideways - which is the one outcome that
        // makes the button look broken while it is working perfectly.
        if (IsPlayingVideo)
        {
            TurnPlayingVideo(turn);
            return;
        }

        TurnedPhoto result;

        // Behind the same gate as naming a face, and for the same reason: both
        // rewrite this picture's face rows, and naming now returns to the screen
        // before its write has finished. Without this, turning a photograph the
        // instant after naming somebody in it is two writers on one SQLite file.
        await _viewerWrites.WaitAsync().ConfigureAwait(true);
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            result = await scope.ServiceProvider
                .GetRequiredService<TurnPhotoHandler>()
                .HandleAsync(tile.ThumbnailName, turn)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            DiagnosticLog.Write($"could not turn {tile.FileName}", ex);
            return;
        }
        finally
        {
            _viewerWrites.Release();
        }

        if (!result.Turned)
        {
            // Nothing moved. When that is because the folder is away it has to be
            // said outright, or the picture simply refuses to turn with no
            // explanation at all - which reads as the button being broken.
            if (result.UnreachableSources.Count > 0)
            {
                TurnRefusedOutOfReach?.Invoke(this, result.UnreachableSources);
            }

            return;
        }

        // Said outright, because "the file itself now knows" and "only this app
        // knows" are different situations and the difference is invisible until
        // the user opens the picture somewhere else.
        bool hereOnly = result.CachedOnly > 0 && result.OriginalsTold == 0;
        TurnNotice = hereOnly ? TurnNotices.HereOnly : null;
        TurnNoticeTip = hereOnly ? TurnNotices.HereOnlyTip : null;

        OpenPicture = TileImageLoader.LoadPreview(_thumbnails, tile.ThumbnailName);
        tile.Picture = TileImageLoader.LoadTile(_thumbnails, tile.ThumbnailName);

        // The boxes have moved on record; this is what re-reads them.
        if (ShowFaceNames)
        {
            await LoadOpenFacesAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// What deleting the open photograph would cost, or null when there is
    /// nothing to delete.
    /// </summary>
    /// <remarks>
    /// Read before the question is asked, so the confirmation can say which file
    /// and how many names rather than asking about "this photo" in the abstract.
    /// The view owns the asking, because a modal dialog is a view's job.
    /// </remarks>
    public async Task<PhotoToRemove?> DescribeDeletionAsync()
    {
        if (OpenTile is not GalleryTile tile)
        {
            return null;
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<RemovePhotoHandler>()
                .DescribeAsync(tile.Item.Id)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            DiagnosticLog.Write($"could not read {tile.FileName} to delete it", ex);
            return null;
        }
    }

    /// <summary>
    /// Catches the viewer up after the open photograph has been deleted.
    /// </summary>
    /// <remarks>
    /// The deleting itself belongs to the shell, which owns the one overlay that
    /// reports it - this is only what the viewer has left to do about it.
    ///
    /// <para>Moves to the next picture rather than closing the viewer, because
    /// deleting is usually done in a run and closing after each one would make
    /// clearing a folder unbearable. The grid is reloaded so the deleted tile
    /// does not sit there as a picture that no longer opens.</para>
    /// </remarks>
    public async Task AfterOpenPhotoDeletedAsync(PhotoRemovalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (OpenTile is not GalleryTile tile)
        {
            return;
        }

        if (result.Deleted == 0)
        {
            DiagnosticLog.Write($"{tile.FileName} is still on disk; nothing was forgotten");
            return;
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);

        // Only the library's own grid can be reloaded from here. A picture
        // opened from somebody's photographs belongs to the People screen, which
        // rebuilds itself on hearing the line above - so the viewer stands down
        // rather than stepping onto a tile that is about to be replaced.
        //
        // It closed on that path before as well, but by accident: the index
        // below was read from the library's grid whatever the viewer had been
        // opened from, and a tile the People screen built is never in it, so the
        // answer was always -1. Said out loud, it stops being a bug that happens
        // to behave and starts being a rule.
        if (!ReferenceEquals(ViewerGrid, _window))
        {
            ClosePhoto();
            return;
        }

        int at = _window.IndexOf(tile);
        await LoadAsync().ConfigureAwait(true);

        // Whatever moved up into its place, or the new last one at the end of a
        // folder. Nothing left to show closes the viewer.
        if (_window.Count == 0 || at < 0)
        {
            ClosePhoto();
            return;
        }

        Open(_window[Math.Clamp(at, 0, _window.Count - 1)]);
    }

    /// <summary>
    /// Public because the shell closes the viewer too: it sits over the content
    /// area, so leaving it open while another section loads shows both at once.
    /// </summary>
    [RelayCommand]
    public void ClosePhoto()
    {
        OpenTile = null;
        OpenPicture = null;
        _viewerSource = null;
    }

    /// <summary>
    /// Moves through the whole result set, not just what has been scrolled past,
    /// so looking at one picture never becomes a reason to go back to the grid.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPhoto() => Step(1);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPhoto() => Step(-1);

    /// <summary>
    /// Where the open picture sits in this grid, or -1 when it came from another
    /// one.
    /// </summary>
    /// <remarks>
    /// The People screen opens pictures in this same viewer, and those tiles are
    /// its grid's rather than the library's. Not-found used to read as index -1
    /// and step to 0, so an arrow key over one person's photograph jumped to the
    /// first picture in the library.
    /// </remarks>
    private int OpenTileIndex => OpenTile is null ? -1 : ViewerGrid.IndexOf(OpenTile);

    private bool CanGoNext() =>
        OpenTileIndex >= 0 && OpenTileIndex < ViewerGrid.Count - 1;

    private bool CanGoPrevious() => OpenTileIndex > 0;

    private void Step(int direction)
    {
        int at = OpenTileIndex;
        if (at < 0)
        {
            return;
        }

        int next = at + direction;
        if (next >= 0 && next < ViewerGrid.Count)
        {
            Open(ViewerGrid[next]);
        }
    }

    partial void OnSelectedFolderChanged(FolderNode? value)
    {
        // Choosing a folder answers a different question from choosing a person
        // or a place, and quietly combining them would show "every photo of my
        // son" minus most of them with nothing on screen saying why.
        if (value is not null)
        {
            PersonId = null;
            PersonName = string.Empty;
            ClearPlace();
        }

        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>
    /// Reverses the grid. The order is applied by the query rather than by
    /// reversing what is already loaded, so the tie-break between the 1,964
    /// photos sharing a timestamp reverses with it.
    /// </summary>
    partial void OnOldestFirstChanged(bool value)
    {
        OnPropertyChanged(nameof(SortOrder));

        if (!_restoringSortOrder)
        {
            _ = LoadAsync();
        }
    }

    /// <summary>
    /// Leaving the folder view drops its filter, so the media view always shows
    /// the whole library. Letting a selection survive the switch meant the grid
    /// silently kept showing one folder while the control said otherwise.
    /// </summary>
    partial void OnShowFoldersChanged(bool value)
    {
        if (!value && SelectedFolder is not null)
        {
            SelectedFolder = null;
            _ = LoadAsync();
        }
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(CountSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

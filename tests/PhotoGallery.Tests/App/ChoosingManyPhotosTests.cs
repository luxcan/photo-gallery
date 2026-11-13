using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Albums;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Choosing several photographs on the wall, to move or to delete together.
/// </summary>
/// <remarks>
/// Tidying a holiday meant opening and closing the viewer a few hundred times,
/// because every way a picture could leave the wall went through it one at a
/// time. Both of the things this does were already built for many photographs -
/// the deleting funnel and the album write have taken lists all along - so what
/// is argued out here is the choosing: what a click means while the mode is on,
/// and what happens to a choice the wall stops being able to honour.
/// </remarks>
public sealed class ChoosingManyPhotosTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly List<ServiceProvider> _built = [];
    private readonly string _root;
    private readonly GalleryViewModel _gallery;

    public ChoosingManyPhotosTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-choosing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    /// <summary>With the mode off, a click still opens the picture.</summary>
    /// <remarks>
    /// The rule this screen was built on. Choosing several cannot be a different
    /// click on the same tile, or one of the two has to lose.
    /// </remarks>
    [Fact]
    public void WithTheModeOff_AClickOpensThePhotograph()
    {
        GalleryTile tile = Tile(1, "a.jpg");

        _gallery.OpenPhotoCommand.Execute(tile);

        Assert.True(_gallery.IsViewerOpen);
        Assert.False(tile.IsChosen);
        Assert.Equal(0, _gallery.ChosenCount);
    }

    /// <summary>With it on, the same click ticks it instead.</summary>
    [Fact]
    public void WithTheModeOn_AClickTicksThePhotograph()
    {
        GalleryTile tile = Tile(1, "a.jpg");
        _gallery.IsChoosing = true;

        _gallery.OpenPhotoCommand.Execute(tile);

        Assert.False(_gallery.IsViewerOpen);
        Assert.True(tile.IsChosen);
        Assert.Equal(1, _gallery.ChosenCount);
        Assert.Equal("1 chosen", _gallery.ChosenSummary);
    }

    /// <summary>And clicking it again unticks it.</summary>
    [Fact]
    public void ClickingATickedPhotographAgainUnticksIt()
    {
        GalleryTile tile = Tile(1, "a.jpg");
        _gallery.IsChoosing = true;

        _gallery.OpenPhotoCommand.Execute(tile);
        _gallery.OpenPhotoCommand.Execute(tile);

        Assert.False(tile.IsChosen);
        Assert.Equal(0, _gallery.ChosenCount);
        Assert.False(_gallery.HasChosen);
    }

    /// <summary>The delete button carries the count, in both numbers.</summary>
    /// <remarks>
    /// The last place the number can be noticed before the question is asked.
    /// </remarks>
    [Fact]
    public void TheDeleteButtonSaysHowManyItWouldDestroy()
    {
        _gallery.IsChoosing = true;

        _gallery.OpenPhotoCommand.Execute(Tile(1, "a.jpg"));
        Assert.Equal("Delete 1 photo", _gallery.DeleteChosenCaption);

        _gallery.OpenPhotoCommand.Execute(Tile(2, "b.jpg"));
        Assert.Equal("Delete 2 photos", _gallery.DeleteChosenCaption);
    }

    /// <summary>Leaving the mode forgets what was ticked.</summary>
    /// <remarks>
    /// A wall that looks untouched and still has forty photographs spoken for
    /// would make the next press of Delete a question about pictures nobody
    /// could see were chosen.
    /// </remarks>
    /// <remarks>
    /// Asserted on the count rather than on the tile handed in. The set of ids
    /// is what this holds; the mark on a picture is a mirror of it, put back
    /// onto whatever tiles the wall has at the time - so a tile that was never
    /// on a wall is not something the model has an opinion about.
    /// </remarks>
    [Fact]
    public void LeavingTheModeForgetsWhatWasTicked()
    {
        _gallery.IsChoosing = true;
        _gallery.OpenPhotoCommand.Execute(Tile(1, "a.jpg"));

        _gallery.StopChoosingCommand.Execute(null);

        Assert.False(_gallery.IsChoosing);
        Assert.Equal(0, _gallery.ChosenCount);
        Assert.False(_gallery.HasChosen);
        Assert.Equal("None chosen", _gallery.ChosenSummary);
    }

    /// <summary>Clearing unticks everything and stays in the mode.</summary>
    [Fact]
    public void ClearingUnticksEverythingAndStaysInTheMode()
    {
        _gallery.IsChoosing = true;
        _gallery.OpenPhotoCommand.Execute(Tile(1, "a.jpg"));

        _gallery.ChooseNothingCommand.Execute(null);

        Assert.True(_gallery.IsChoosing);
        Assert.Equal(0, _gallery.ChosenCount);
        Assert.False(_gallery.HasChosen);
    }

    /// <summary>Nothing is ticked to begin with, and the strip says so.</summary>
    [Fact]
    public void NothingIsTickedToBeginWith()
    {
        Assert.False(_gallery.IsChoosing);
        Assert.False(_gallery.HasChosen);
        Assert.Equal("None chosen", _gallery.ChosenSummary);
    }

    /// <summary>
    /// The album sentence names the album and where they came from.
    /// </summary>
    /// <remarks>
    /// A photograph belongs to one album, so putting it somewhere takes it out
    /// of wherever it was. That is nobody's expectation until it happens to
    /// them, so it is said rather than done quietly - and the album is named
    /// because the wall the reader is standing on is the one they just left.
    /// </remarks>
    [Theory]
    [InlineData(1, 0, "Added to Bali")]
    [InlineData(12, 0, "12 photographs added to Bali.")]
    [InlineData(1, 1, "Moved into Bali, out of March 2019")]
    [InlineData(12, 12, "12 photographs moved into Bali, out of March 2019.")]
    [InlineData(12, 5, "12 photographs added to Bali, 5 of them out of March 2019.")]
    public void TheAlbumSentenceNamesTheAlbumAndWhereTheyCameFrom(
        int placed, int moved, string expected)
    {
        var result = new AlbumAddResult(placed, moved, moved == 0 ? [] : ["March 2019"]);

        Assert.Equal(expected, AlbumMoveNotice.For("Bali", placed, result));
    }

    /// <summary>
    /// The bar along the bottom carries the controls.
    /// </summary>
    /// <remarks>
    /// Markup, so no view-model test can see it: every property above can be
    /// right while the bar is bound to none of them, because a WPF binding that
    /// names nothing fails in silence.
    ///
    /// <para>Asserted on the bar rather than on the window, because "it is
    /// somewhere in this file" is exactly what was true while these controls
    /// were being painted over by their neighbours.</para>
    /// </remarks>
    [Fact]
    public void TheBarCarriesTheChoosingControls()
    {
        string bar = Bar(File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml")));

        Assert.Contains("Gallery.ChosenSummary", bar, StringComparison.Ordinal);
        Assert.Contains("Gallery.ChooseEverythingCommand", bar, StringComparison.Ordinal);
        Assert.Contains("Gallery.ChooseNothingCommand", bar, StringComparison.Ordinal);
        Assert.Contains("Gallery.MoveChosenToAlbumCommand", bar, StringComparison.Ordinal);
        Assert.Contains("Gallery.DeleteChosenCaption", bar, StringComparison.Ordinal);
        Assert.Contains("OnDeleteChosenPhotosClicked", bar, StringComparison.Ordinal);
        Assert.Contains("Gallery.StopChoosingCommand", bar, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bar is up only on the wall, and only while something is being chosen.
    /// </summary>
    /// <remarks>
    /// It floats over whichever screen is open, so gated on the mode alone it
    /// would follow somebody to People and sit there offering to delete
    /// photographs they can no longer see.
    /// </remarks>
    [Fact]
    public void TheBarBelongsToTheWallAndToTheMode()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        Assert.Contains(
            "Visibility=\"{Binding ShowChoosingBar,", window, StringComparison.Ordinal);

        // And the two things that have to keep out from under it.
        Assert.Contains("Margin=\"{Binding WallMargin}\"", window, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{Binding NoticeMargin}\"", window, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bar does not cover the side nav, and everything modal covers the bar.
    /// </summary>
    /// <remarks>
    /// Drawn as a child of the window it would lie across the nav, which is how
    /// somebody leaves a wall - and a selection survives leaving and comes back,
    /// so there is nothing to protect them from. The viewer, the album picker
    /// and the pass overlay are children of the window and are declared after
    /// the content, so they still cover this.
    /// </remarks>
    [Fact]
    public void TheBarSitsOverThePicturesAndUnderEverythingModal()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        int bar = window.IndexOf(
            "Visibility=\"{Binding ShowChoosingBar,", StringComparison.Ordinal);
        int status = window.IndexOf("<!-- Status bar.", StringComparison.Ordinal);
        int viewer = window.IndexOf("x:Name=\"PhotoViewer\"", StringComparison.Ordinal);
        int picker = window.IndexOf(
            "Content=\"{Binding Gallery.Albums}\"", StringComparison.Ordinal);

        Assert.True(bar > 0 && status > 0 && viewer > 0 && picker > 0, "the window has changed shape");

        // Inside the content area, which ends where the status bar begins.
        Assert.True(bar < status, "the bar is a child of the window, so it lies across the nav");

        // And under both of the things that are drawn over a wall.
        Assert.True(viewer > bar && picker > bar, "something modal now opens underneath the bar");
    }

    /// <summary>
    /// The way out of a selection is named as the twin of the way in.
    /// </summary>
    /// <remarks>
    /// It said "Clear", a 12px link an inch from the search box - so it read as
    /// clearing the search, and somebody who had just pressed Select all
    /// reported that there was no way to unselect. Both things were there and
    /// both worked; neither looked like what it was. "Select none" can only mean
    /// one thing beside "Select all".
    ///
    /// <para>And Select all switches itself off once everything is ticked. It
    /// stayed lit and did nothing, which is how pressing it again - the obvious
    /// gesture - taught somebody that they were stuck.</para>
    /// </remarks>
    [Fact]
    public void TheWayOutOfASelectionIsNamedAsThePairOfTheWayIn()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));
        string bar = Bar(window);

        Assert.Contains("Content=\"Select all\"", bar, StringComparison.Ordinal);
        Assert.Contains("Content=\"Select none\"", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Clear\"", window, StringComparison.Ordinal);

        Assert.Contains(
            "IsEnabled=\"{Binding Gallery.CanChooseEverything}\"",
            bar,
            StringComparison.Ordinal);

        // And the two gestures nothing on screen used to mention. Shift is only
        // allowed to exist because the bar says it does.
        Assert.Contains("Escape leaves choosing altogether", bar, StringComparison.Ordinal);
        Assert.Contains("Shift-click for a run", bar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Filing a selection says what it does.
    /// </summary>
    /// <remarks>
    /// "Move to album" understates the only way to file a selection, and sits
    /// two controls from a Folders toggle - so "move" and "folder" did the
    /// confusing between them, and somebody looking for how to put photographs
    /// in a folder did not find the button that does it.
    /// </remarks>
    [Fact]
    public void FilingASelectionIsCalledPuttingItInAnAlbum()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        Assert.Contains("Content=\"Put in album\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Move to album\"", window, StringComparison.Ordinal);
    }

    /// <summary>
    /// Choosing has two controls, and neither of them is a pill in the strip.
    /// </summary>
    /// <remarks>
    /// It starts on the photograph - the circle appears when the pointer is over
    /// a tile - and ends at the bar along the bottom. A Select pill up in the
    /// strip was a third control for a thing with two, and the only one of the
    /// three that was never where the eye already was.
    ///
    /// <para>Its label came from a property that answered "Select" or "Cancel"
    /// depending on the mode. With the pill gone that property could only ever
    /// answer one way, so the word is written where it is read and the dead half
    /// is not kept alive by a test.</para>
    /// </remarks>
    [Fact]
    public void TheStripNoLongerCarriesASelectPill()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        Assert.DoesNotContain("Gallery.ChoosingLabel", window, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IsChecked=\"{Binding Gallery.IsChoosing", window, StringComparison.Ordinal);

        // The way out stays, and says what leaving costs.
        string bar = Bar(window);

        Assert.Contains("Content=\"Cancel\"", bar, StringComparison.Ordinal);
        Assert.Contains("forget what is ticked", bar, StringComparison.Ordinal);
        Assert.Contains("Escape does this too", bar, StringComparison.Ordinal);
    }

    /// <summary>
    /// The accent is on filing a selection, not on destroying it.
    /// </summary>
    /// <remarks>
    /// Putting photographs in an album is the ordinary reason to have chosen
    /// several; deleting them is the rare one, and the only one nobody can undo.
    /// The brightest control on the strip should not be the irreversible one -
    /// it was, and next to a subdued "Move to album" the eye went to Delete
    /// every time.
    ///
    /// <para>Asserted because "make the two actions match" is a plausible tidy-up
    /// that would put it straight back.</para>
    /// </remarks>
    [Fact]
    public void FilingIsAccentedAndDeletingIsNot()
    {
        string bar = Bar(File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml")));

        int album = bar.IndexOf("Content=\"Put in album\"", StringComparison.Ordinal);
        int delete = bar.IndexOf(
            "Content=\"{Binding Gallery.DeleteChosenCaption}\"", StringComparison.Ordinal);

        Assert.True(album > 0 && delete > album, "the two actions are not where they were");

        Assert.Equal("PrimaryButton", StyleBefore(bar, album));
        Assert.Equal("SecondaryButton", StyleBefore(bar, delete));

        // And the way out is not the next thing along from the delete: a rule
        // stands between them, so the control that undoes the mode and the one
        // that destroys photographs are not neighbours.
        int way = bar.IndexOf(
            "Command=\"{Binding Gallery.StopChoosingCommand}\"", StringComparison.Ordinal);
        int rule = bar.IndexOf("Background=\"{DynamicResource Separator}\"", delete, StringComparison.Ordinal);

        Assert.True(way > delete, "the way out is no longer last");
        Assert.True(rule > delete && rule < way, "nothing separates the way out from the delete");
    }

    /// <summary>The style on the button whose content is at this offset.</summary>
    private static string StyleBefore(string markup, int at)
    {
        int style = markup.LastIndexOf(
            "Style=\"{DynamicResource ", at, StringComparison.Ordinal);
        Assert.True(style >= 0, "that button carries no style");

        int from = style + "Style=\"{DynamicResource ".Length;
        int to = markup.IndexOf('}', from);

        return markup[from..to];
    }

    /// <summary>
    /// While choosing, the header is out of use and still readable.
    /// </summary>
    /// <remarks>
    /// Every one of these rebuilds the wall, and the rebuild keeps only what the
    /// new query still shows - so searching, re-sorting, switching between Media
    /// and Folders or pressing a filter chip each silently discards part of a
    /// selection. Out of reach while choosing is where they belong.
    ///
    /// <para>They used to be hidden outright, which was the room the choosing
    /// controls needed: those shared the strip's one elastic column and were
    /// painted over by the opaque panels further right, so on an ordinary window
    /// every button after the count was invisible and unclickable. That is what
    /// was reported as the app having no way to unselect and no way to file a
    /// selection - both worked perfectly, neither could be seen. The bar along
    /// the bottom is the room now, so these can stay: which person, which place
    /// and which search this wall is narrowed to is part of deciding what to
    /// tick.</para>
    ///
    /// <para>Counted rather than merely found, because four of the five is the
    /// state that looks fixed and is not - one control left live is one more way
    /// to throw a selection away by accident.</para>
    /// </remarks>
    [Fact]
    public void WhileChoosing_TheHeaderIsOutOfUseAndStillReadable()
    {
        string header = Header(File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml")));

        // Five: the search box, the count-and-filters line, the sort pair, the
        // view pair, and the row of chips under them.
        int stopped = Regex.Matches(
            header,
            @"IsEnabled=""\{Binding Gallery\.IsChoosing,\s*"
            + @"Converter=\{StaticResource InverseBool\}\}""").Count;

        Assert.Equal(5, stopped);

        // And nothing leaves any more. The Select pill was the one control here
        // that vanished rather than greyed out, because the bar had taken its
        // job; it has since gone altogether, and a header that half disappears
        // and half fades is two rules where one will do.
        int hidden = Regex.Matches(
            header,
            @"Visibility=""\{Binding Gallery\.IsChoosing,\s*"
            + @"Converter=\{StaticResource InverseBoolToVisibility\}\}""").Count;

        Assert.Equal(0, hidden);
    }

    /// <summary>
    /// A control that is out of use looks out of use.
    /// </summary>
    /// <remarks>
    /// Switching the header off rather than hiding it is only honest if it shows.
    /// None of these three had a disabled state at all, so a switched-off
    /// segment, chip or search box looked exactly as live as a working one and
    /// simply ignored the pointer - which reads as the app being broken.
    /// </remarks>
    [Fact]
    public void AControlThatIsOutOfUseLooksOutOfUse()
    {
        string controls = File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));

        foreach (string style in new[] { "SegmentedToggle", "PillToggle", "InputTextBox" })
        {
            int at = controls.IndexOf($"x:Key=\"{style}\"", StringComparison.Ordinal);
            Assert.True(at > 0, $"{style} has gone missing");

            int next = controls.IndexOf("\r\n  <Style x:Key=", at, StringComparison.Ordinal);
            string body = next > at ? controls[at..next] : controls[at..];

            Assert.Contains(
                "<Trigger Property=\"IsEnabled\" Value=\"False\">", body, StringComparison.Ordinal);
        }
    }

    /// <summary>The header above the wall: the strip, the chips, and the notices.</summary>
    private static string Header(string window)
    {
        int start = window.IndexOf("<Grid Grid.Row=\"0\" Margin=\"0,0,0,10\">", StringComparison.Ordinal);
        Assert.True(start > 0, "the strip is not in the window");

        int end = window.IndexOf("x:Name=\"GalleryGrid\"", start, StringComparison.Ordinal);
        Assert.True(end > start, "the header does not end where it should");

        return window[start..end];
    }

    /// <summary>The bar along the bottom, from its gate to the status bar below.</summary>
    private static string Bar(string window)
    {
        int start = window.IndexOf(
            "Visibility=\"{Binding ShowChoosingBar,", StringComparison.Ordinal);
        Assert.True(start > 0, "there is no bar along the bottom");

        int end = window.IndexOf("<!-- Status bar.", start, StringComparison.Ordinal);
        Assert.True(end > start, "the bar does not end where it should");

        return window[start..end];
    }

    /// <summary>
    /// Select all is off when there is nothing to select, not merely when
    /// everything already is.
    /// </summary>
    [Fact]
    public void SelectAllIsOffWhenTheWallIsEmpty()
    {
        _gallery.IsChoosing = true;

        Assert.False(_gallery.CanChooseEverything);
        Assert.False(_gallery.EverythingChosen);
    }

    /// <summary>A ticked photograph is marked on the picture itself.</summary>
    /// <remarks>
    /// On the tile, because the rows virtualise with container recycling - a
    /// mark held by the container would wander onto another photograph the
    /// moment the wall was scrolled.
    /// </remarks>
    [Fact]
    public void ATickedPhotographIsMarkedOnThePicture()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));
        string controls = File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));

        Assert.Contains("ChosenTick", window, StringComparison.Ordinal);
        Assert.Contains("ChosenVeil", window, StringComparison.Ordinal);
        Assert.Contains("IsChosen", window, StringComparison.Ordinal);

        Assert.Contains("x:Key=\"ChosenTick\"", controls, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ChosenVeil\"", controls, StringComparison.Ordinal);
    }

    /// <summary>
    /// Putting a batch in an album is handed to the shell, not done here.
    /// </summary>
    /// <remarks>
    /// The write is quick and the wall rebuilt afterwards is not, so it wants
    /// the overlay - and the overlay is the shell's, for the reason keeping a
    /// duplicate group is the shell's. Done here it would run with the window
    /// live, leaving the moved photographs on screen looking untouched and
    /// every other control still clickable.
    /// </remarks>
    [Fact]
    public async Task PuttingABatchInAnAlbumIsHandedToTheShell()
    {
        GalleryViewModel gallery = WithAlbums(new OneAlbum());

        string? asked = null;
        gallery.PlacingChosen += (_, name) => asked = name;

        gallery.IsChoosing = true;
        gallery.OpenPhotoCommand.Execute(Tile(1, "a.jpg"));
        gallery.OpenPhotoCommand.Execute(Tile(2, "b.jpg"));

        await gallery.MoveChosenToAlbumCommand.ExecuteAsync(null);
        await gallery.Albums.ChooseCommand.ExecuteAsync(gallery.Albums.Choices[0]);

        Assert.Equal("Bali", asked);

        // And nothing was written on the way past: the shell writes, under the
        // overlay, when it calls back.
        Assert.Equal(0, OneAlbum.Added);
    }

    /// <summary>
    /// The picker names how many it is about to place.
    /// </summary>
    [Fact]
    public async Task ThePickerSaysHowManyItIsPlacing()
    {
        GalleryViewModel gallery = WithAlbums(new OneAlbum());

        gallery.IsChoosing = true;
        gallery.OpenPhotoCommand.Execute(Tile(1, "a.jpg"));
        gallery.OpenPhotoCommand.Execute(Tile(2, "b.jpg"));

        await gallery.MoveChosenToAlbumCommand.ExecuteAsync(null);

        Assert.Equal("Put these 2 photographs in an album", gallery.Albums.Prompt);

        // No album to leave, because two photographs may have come from two
        // places - so the way out of one hides itself.
        Assert.False(gallery.Albums.IsInOne);
    }

    /// <summary>
    /// The album picker is hosted where the wall can show it, not only the
    /// viewer.
    /// </summary>
    /// <remarks>
    /// It used to live inside the viewer's own chrome, which is gated on a
    /// photograph being open - so asking a wall of ticked pictures which album
    /// they belong in put a panel on screen that nobody could see. The bug was
    /// invisible to every view-model test: the picker said it was open, and it
    /// was, in a part of the window that was not being drawn.
    /// </remarks>
    [Fact]
    public void ThePickerIsHostedWhereTheWallCanShowIt()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        int middle = window.IndexOf(
            "Visibility=\"{Binding Gallery.Albums.IsOpenInTheMiddle,", StringComparison.Ordinal);
        int viewer = window.IndexOf(
            "Visibility=\"{Binding Gallery.IsViewerOpen,", StringComparison.Ordinal);

        Assert.True(middle > 0 && viewer > 0, "the markup no longer has both");
        Assert.True(
            middle > viewer,
            "the picker is inside the viewer again, so a wall cannot show it");
    }

    /// <summary>
    /// One picker and one template, drawn in either of two places.
    /// </summary>
    /// <remarks>
    /// "One picker, one host" was written to stop a second list being kept in
    /// step - a second place to get the wording, the filter and the
    /// make-a-new-one box wrong. That still holds: there is one
    /// <c>DataTemplate</c> and one view model, and what differs between the two
    /// places is the chrome around them and nothing else.
    ///
    /// <para>They cannot both be open, because the flags they are gated on are
    /// the two halves of one question the view model answers.</para>
    /// </remarks>
    [Fact]
    public void ThePickerIsOneTemplateDrawnInEitherOfTwoPlaces()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));
        string controls = File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));

        // One template, wherever it is drawn.
        Assert.Equal(
            1, Occurrences(controls, "<DataTemplate DataType=\"{x:Type albums:AlbumPicker}\">"));

        // Two hosts, both of them the same view model.
        Assert.Equal(2, Occurrences(window, "Content=\"{Binding Gallery.Albums}\""));

        // And each gated on its own half, so neither can draw over the other.
        Assert.Contains(
            "Gallery.Albums.IsOpenInTheMiddle", window, StringComparison.Ordinal);
        Assert.Contains(
            "Gallery.Albums.IsOpenBesideTheBar", window, StringComparison.Ordinal);

        // The bar's one rises from the bar, and dismisses like a popover rather
        // than holding the window until it is answered.
        int popup = window.IndexOf(
            "IsOpen=\"{Binding Gallery.Albums.IsOpenBesideTheBar", StringComparison.Ordinal);
        int from = window.LastIndexOf("<Popup ", popup, StringComparison.Ordinal);

        Assert.True(popup > 0 && from > 0, "the bar's picker is no longer a popover");

        string popover = window[from..popup];

        Assert.Contains("ElementName=ChoosingBar", popover, StringComparison.Ordinal);
        Assert.Contains("StaysOpen=\"False\"", popover, StringComparison.Ordinal);
    }

    /// <summary>
    /// The click that dismisses the popover does nothing else.
    /// </summary>
    /// <remarks>
    /// StaysOpen closes the panel on an outside click but does not swallow that
    /// click, and underneath it is a wall of tickable photographs and a button
    /// that deletes them: closing the list by clicking the wall ticked another
    /// picture, so changing your mind about the album quietly cost a fifth
    /// photograph. Four became five while this was being driven.
    ///
    /// <para>A transparent sheet is what catches it - transparent so the
    /// photographs being filed stay visible, which is the entire reason this is
    /// a popover rather than the modal it replaced.</para>
    /// </remarks>
    [Fact]
    public void TheClickThatDismissesThePopoverDoesNothingElse()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));
        string shell = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml.cs"));

        int sheet = window.IndexOf(
            "MouseLeftButtonDown=\"OnAlbumPopoverDismissed\"", StringComparison.Ordinal);
        Assert.True(sheet > 0, "nothing catches the click that closes the popover");

        // Transparent rather than shaded, and hit-testable rather than null:
        // a Background of nothing is not something a click can land on.
        int opens = window.LastIndexOf("<Grid ", sheet, StringComparison.Ordinal);
        Assert.Contains(
            "Background=\"Transparent\"", window[opens..sheet], StringComparison.Ordinal);
        Assert.DoesNotContain("ModalScrim", window[opens..sheet], StringComparison.Ordinal);

        // And the click stops there.
        int handler = shell.IndexOf(
            "private void OnAlbumPopoverDismissed", StringComparison.Ordinal);
        Assert.True(handler > 0, "the shell does not answer that click");

        int ends = shell.IndexOf("\r\n    }", handler, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", shell[handler..ends], StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value)
    {
        int found = 0;

        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }

    private GalleryViewModel WithAlbums(IAlbumRepository albums)
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(albums)
            .BuildServiceProvider();

        _built.Add(services);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        return new GalleryViewModel(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    private static GalleryTile Tile(int id, string fileName) =>
        new(new GalleryItem(
            id,
            $@"holiday\{fileName}",
            fileName,
            "holiday",
            $@"C:\pictures\holiday\{fileName}",
            fileName,
            null,
            new DateTime(2012, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            0,
            AssetKind.Photo));

    /// <summary>
    /// One album to choose, and a count of what was actually written.
    /// </summary>
    /// <remarks>
    /// Everything else throws rather than answering quietly, so a path that
    /// reaches the repository by a route these tests did not intend says so.
    /// </remarks>
    private sealed class OneAlbum : IAlbumRepository
    {
        public static int Added { get; private set; }

        public OneAlbum() => Added = 0;

        public Task<IReadOnlyList<AlbumSummary>> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AlbumSummary>>(
            [
                new AlbumSummary(
                    7, "Bali", DateTime.UnixEpoch, DateTime.UnixEpoch,
                    AlbumKind.Trip, AlbumOrigin.Made, 3, CoverThumbnailName: null),
            ]);

        public Task<AlbumAddResult> AddAsync(
            int albumId, IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(assetIds);
            Added += assetIds.Count;

            return Task.FromResult(new AlbumAddResult(assetIds.Count, 0, []));
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

        public Task<IReadOnlyList<int>> GetMembersAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AlbumRule> GetRuleAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetRuleAsync(
            int albumId, AlbumRule rule, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<int>> SuggestAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<int>> GetMembersAmongAsync(
            int albumId, IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<int>> StillSuggestedAsync(
            int albumId, IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default) =>
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

    public void Dispose()
    {
        _services.Dispose();

        foreach (ServiceProvider services in _built)
        {
            services.Dispose();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not go is not a failed test.
        }
    }
}

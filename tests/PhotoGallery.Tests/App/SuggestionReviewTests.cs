namespace PhotoGallery.Tests.App;

/// <summary>
/// Judging an album's proposals at the size a photograph can actually be judged.
/// </summary>
/// <remarks>
/// A proposal was 110 pixels in a strip and nothing else. That is too little to
/// tell whether a photograph belongs, and for a video it showed a poster with no
/// way to say it was even a video. Opening one was not offered at all, and would
/// not have helped on its own: the answer lived on the tile, so deciding meant
/// closing the viewer and finding that tile again among two hundred others.
///
/// <para>Answering is two buttons rather than a switch, and the proposal leaves
/// the list when either is pressed: at full size this is a queue worked through
/// one at a time, not a screenful judged at a glance, so the next question should
/// already be on screen.</para>
///
/// <para>Most of that is markup, which is why it is asserted here as text rather
/// than exercised - a binding is not behaviour and no view-model test can see
/// one.</para>
/// </remarks>
public sealed class SuggestionReviewTests
{
    [Fact]
    public void AProposalCanBeOpened()
    {
        string strip = SuggestionStrip();

        Assert.Contains("CropAction", strip, StringComparison.Ordinal);
        Assert.Contains("OpenSuggestedPhotoCommand", strip, StringComparison.Ordinal);
    }

    /// <summary>
    /// Its own command, not the one the album's own photographs use.
    /// </summary>
    /// <remarks>
    /// The arrows in the viewer walk one list. Opened through
    /// OpenAlbumPhotoCommand, a proposal would step into the photographs
    /// already in the album - a different set, and never the one being reviewed.
    /// </remarks>
    [Fact]
    public void ProposalsAreSteppedThroughSeparatelyFromTheAlbum()
    {
        Assert.DoesNotContain(
            "OpenAlbumPhotoCommand", SuggestionStrip(), StringComparison.Ordinal);
    }

    [Fact]
    public void AProposedVideoSaysSo()
    {
        Assert.Contains("VideoBadge", SuggestionStrip(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The answer is given on the picture, as two acts rather than a switch.
    /// </summary>
    [Fact]
    public void TheAnswerCanBeGivenWithoutLeavingThePicture()
    {
        string window = Window();

        Assert.Contains("Gallery.IsDecidingSuggestion", window, StringComparison.Ordinal);
        Assert.Contains("KeepSuggestedPhotoCommand", window, StringComparison.Ordinal);
        Assert.Contains("RefuseSuggestedPhotoCommand", window, StringComparison.Ordinal);
    }

    /// <summary>
    /// Answering removes the proposal from the list rather than marking it.
    /// </summary>
    /// <remarks>
    /// The whole point of the two buttons: a switch leaves the picture in the
    /// strip wearing its answer, which is right for a screenful judged at a
    /// glance and wrong for a queue worked through one at a time. If this ever
    /// stops removing, the viewer shows the same photograph after answering it.
    /// </remarks>
    [Fact]
    public void AnsweringTakesTheProposalOutOfTheList()
    {
        string source = File.ReadAllText(
            AppMarkup.PathTo("Albums", "AlbumsViewModel.cs"));

        Assert.Contains("Suggestions.Remove(tile)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every name the new markup binds to exists on the object it binds against.
    /// </summary>
    /// <remarks>
    /// A binding to a name that is not there does not throw and does not fail the
    /// build. The control simply renders empty or does nothing when pressed, and
    /// the only record of it is a line in the diagnostic log nobody has switched
    /// on. That failure mode is why the markup above is asserted as text; this is
    /// the other half - the text is only right if these names are real.
    /// </remarks>
    [Theory]
    [InlineData(typeof(PhotoGallery.App.ViewModels.MainViewModel), "OpenSuggestedPhotoCommand")]
    [InlineData(typeof(PhotoGallery.App.Gallery.GalleryViewModel), "IsDecidingSuggestion")]
    [InlineData(typeof(PhotoGallery.App.ViewModels.MainViewModel), "KeepSuggestedPhotoCommand")]
    [InlineData(typeof(PhotoGallery.App.ViewModels.MainViewModel), "RefuseSuggestedPhotoCommand")]
    [InlineData(typeof(PhotoGallery.App.Gallery.GalleryTile), "IsVideo")]
    [InlineData(typeof(PhotoGallery.App.Gallery.GalleryTile), "IsChosen")]
    [InlineData(typeof(PhotoGallery.App.Albums.AlbumsViewModel), "SuggestionGrid")]
    [InlineData(typeof(PhotoGallery.App.Albums.AlbumPicker), "Narrowed")]
    [InlineData(typeof(PhotoGallery.App.Albums.AlbumPicker), "IsNarrowed")]
    public void EveryNameTheMarkupBindsToExists(Type owner, string member)
    {
        Assert.True(
            owner.GetProperty(member) is not null,
            $"{owner.Name} has no public {member}; the binding that names it will fail silently.");
    }

    private static string Window() =>
        File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    /// <summary>The strip of proposals, from the ItemsControl that renders it.</summary>
    private static string SuggestionStrip()
    {
        string window = Window();

        int start = window.IndexOf(
            "ItemsSource=\"{Binding Albums.Suggestions}\"", StringComparison.Ordinal);

        Assert.True(start >= 0, "The strip of proposals has been renamed or removed.");

        int end = window.IndexOf("</ItemsControl>", start, StringComparison.Ordinal);
        return window[start..end];
    }
}

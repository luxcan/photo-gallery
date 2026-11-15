namespace PhotoGallery.Tests.App;

/// <summary>
/// The card that says the Library wall is being read, read as text because a
/// binding to a name that does not exist fails silently.
/// </summary>
/// <remarks>
/// Asked for after the faded wall alone read as nothing happening: the same
/// card the long passes use, centred over the wall, shown while it loads.
/// </remarks>
public sealed class LibraryLoadingCardMarkupTests
{
    private static readonly string s_window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    [Fact]
    public void TheWallSaysItIsLoadingInThePanelsOwnCard()
    {
        int title = s_window.IndexOf("Text=\"Loading your pictures\"", StringComparison.Ordinal);
        Assert.True(title > 0, "the loading card is gone");

        int opens = s_window.LastIndexOf("<Grid HorizontalAlignment=\"Center\"", title, StringComparison.Ordinal);
        Assert.True(opens > 0, "the loading card's outline has moved");
        string card = s_window[opens..title];

        Assert.Contains("<Border Style=\"{DynamicResource ModalSurface}\"", card, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding Gallery.IsLoading,", card, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"", card, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", card, StringComparison.Ordinal);
        Assert.Contains("Style=\"{DynamicResource ModalTitle}\"", s_window[title..(title + 200)], StringComparison.Ordinal);
    }

    [Fact]
    public void TheCardIsLaidOutLikeTheLongPassPanel()
    {
        // Same second line and the same bar spacing as the panel that says
        // "Saving your answers", so the two cannot drift apart in size.
        int title = s_window.IndexOf("Text=\"Loading your pictures\"", StringComparison.Ordinal);
        string after = s_window[title..s_window.IndexOf("</StackPanel>", title, StringComparison.Ordinal)];

        Assert.Contains(
            "Text=\"{Binding Gallery.LoadingTarget}\" Margin=\"0,6,0,0\" FontSize=\"12\"",
            after, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource Editor.Placeholder}\"", after, StringComparison.Ordinal);
        Assert.Contains("Style=\"{DynamicResource PassProgress}\" Margin=\"0,18,0,0\"", after, StringComparison.Ordinal);

        int panel = s_window.IndexOf("Text=\"{Binding OverlayTarget}\" Margin=\"0,6,0,0\" FontSize=\"12\"", StringComparison.Ordinal);
        Assert.True(panel > 0, "the long-pass panel's second line changed; keep the card in step with it");
    }
}

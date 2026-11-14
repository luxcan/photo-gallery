namespace PhotoGallery.Tests.App;

/// <summary>
/// That the overlay draws its Stop button only for work that can be stopped.
/// </summary>
/// <remarks>
/// Read as text, because a binding to a name that does not exist fails
/// silently and the button would simply be there again.
/// </remarks>
public sealed class OverlayStopMarkupTests
{
    [Fact]
    public void TheStopButtonIsShownOnlyWhenStopIsOffered()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        int stop = window.IndexOf("Command=\"{Binding CancelPassCommand}\"", StringComparison.Ordinal);
        Assert.True(stop > 0, "the overlay's Stop button cannot be found");

        int opens = window.LastIndexOf("<Button ", stop, StringComparison.Ordinal);
        string button = window[opens..stop];
        Assert.Contains("Visibility=\"{Binding OffersStop,", button, StringComparison.Ordinal);
    }
}

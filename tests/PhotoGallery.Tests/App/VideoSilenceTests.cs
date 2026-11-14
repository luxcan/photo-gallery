namespace PhotoGallery.Tests.App;

/// <summary>
/// That a clip nobody pressed play on is never heard.
/// </summary>
/// <remarks>
/// Read as text, because the element lives in the code-behind and no view-model
/// test can see it. The bug: play one video, close the viewer, open another - and
/// its sound started before play was pressed. A Manual <c>MediaElement</c>
/// remembers that it was told to play, so the next clip handed to it to warm
/// started by itself.
/// </remarks>
public sealed class VideoSilenceTests
{
    private static readonly string s_code =
        File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml.cs"));

    [Fact]
    public void ThePlayerIsPausedWheneverPlayingEnds()
    {
        int hook = s_code.IndexOf(
            "e.PropertyName == nameof(GalleryViewModel.IsPlayingVideo)", StringComparison.Ordinal);
        Assert.True(hook >= 0, "Nothing watches for playing to end.");

        string body = s_code[hook..s_code.IndexOf("};", hook, StringComparison.Ordinal)];
        Assert.Contains("!viewModel.Gallery.IsPlayingVideo", body, StringComparison.Ordinal);
        Assert.Contains("VideoPlayer.Pause();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AClipOpenedOnlyToWarmItIsHeldPaused()
    {
        int start = s_code.IndexOf("private async void OnVideoOpened(", StringComparison.Ordinal);
        Assert.True(start >= 0, "OnVideoOpened has been renamed or removed.");

        int next = s_code.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        string handler = next < 0 ? s_code[start..] : s_code[start..next];

        int asked = handler.IndexOf("if (_viewModel.Gallery.IsPlayingVideo)", StringComparison.Ordinal);
        int otherwise = handler.IndexOf("else", asked, StringComparison.Ordinal);
        Assert.True(asked >= 0 && otherwise > asked, "OnVideoOpened no longer decides whether it was asked.");
        Assert.Contains("VideoPlayer.Pause();", handler[otherwise..], StringComparison.Ordinal);
    }
}

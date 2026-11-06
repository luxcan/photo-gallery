using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PhotoGallery.App.Theme;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Every tooltip in the app appears after the same short delay.
/// </summary>
/// <remarks>
/// Read from the property's metadata rather than from a live control, so no
/// window or UI thread is needed - and metadata is what decides the default for
/// every element, including ones added after this test was written.
/// </remarks>
public sealed class ToolTipTimingTests
{
    [Fact]
    public void TheDelayIsAQuarterOfASecond() =>
        Assert.Equal(250, ToolTipTiming.InitialShowDelay);

    [Theory]
    [InlineData(typeof(FrameworkElement))]
    [InlineData(typeof(Button))]
    [InlineData(typeof(Image))]
    [InlineData(typeof(TextBlock))]
    [InlineData(typeof(Hyperlink))]
    [InlineData(typeof(Run))]
    public void OnceApplied_EveryKindOfElementWaitsThatLong(Type element)
    {
        ToolTipTiming.Apply();

        object delay = ToolTipService.InitialShowDelayProperty.GetMetadata(element).DefaultValue;

        Assert.Equal(ToolTipTiming.InitialShowDelay, delay);
    }

    [Fact]
    public void ApplyingTwice_DoesNotThrow()
    {
        ToolTipTiming.Apply();

        Exception? thrown = Record.Exception(ToolTipTiming.Apply);

        Assert.Null(thrown);
    }
}

using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Sharing;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What the Sharing screen says the small copies would cost.
/// </summary>
/// <remarks>
/// The sentence exists to answer one question: is it worth taking the copies
/// another computer has already made, rather than reading every original again.
/// It answered by quoting both costs - and the estimates are rounded to the
/// nearest useful word, so for anything under about nine minutes' work both
/// rounded to "a minute" and the screen offered a choice with the same figure on
/// each side. Fourteen photographs outstanding is the case that showed it.
/// </remarks>
public sealed class PicturesCostTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly SharingViewModel _sharing;

    public PicturesCostTests() =>
        _sharing = new SharingViewModel(_services.GetRequiredService<IServiceScopeFactory>());

    /// <summary>
    /// A handful is said once, because there is nothing to choose between.
    /// </summary>
    [Fact]
    public void WhenBothWaysCostTheSame_TheFigureIsGivenOnce()
    {
        _sharing.Unprepared = 14;

        string label = _sharing.PicturesLabel;

        Assert.Contains("14 photos have no small copy here yet", label, StringComparison.Ordinal);
        Assert.Contains("either way", label, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(label, "about a minute"));
    }

    /// <summary>
    /// A library's worth is the case the comparison was written for.
    /// </summary>
    /// <remarks>
    /// 15,823 photographs is this library: about three quarters of an hour of
    /// reading originals against five minutes of copying. That is the whole
    /// argument for the button, and it has to survive the branch above.
    /// </remarks>
    [Fact]
    public void WhenOneWayIsPlainlyCheaper_BothAreQuoted()
    {
        _sharing.Unprepared = 15_823;

        string label = _sharing.PicturesLabel;

        Assert.Contains("44 minutes", label, StringComparison.Ordinal);
        Assert.Contains("5 minutes", label, StringComparison.Ordinal);
        Assert.DoesNotContain("either way", label, StringComparison.Ordinal);
    }

    /// <summary>One photograph is not "1 photos have".</summary>
    [Fact]
    public void OnePhotographIsSaidAsOne()
    {
        _sharing.Unprepared = 1;

        Assert.Contains(
            "1 photo has no small copy here yet", _sharing.PicturesLabel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reassurance is on every branch, because it is the thing people ask.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(14)]
    [InlineData(15_823)]
    public void ItAlwaysSaysTheOriginalsStayHere(int unprepared)
    {
        _sharing.Unprepared = unprepared;

        Assert.Contains(
            "Your photographs themselves are never copied",
            _sharing.PicturesLabel,
            StringComparison.Ordinal);
    }

    public void Dispose() => _services.Dispose();

    private static int Occurrences(string text, string value)
    {
        int found = 0;

        for (int at = text.IndexOf(value, StringComparison.Ordinal);
             at >= 0;
             at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }
}

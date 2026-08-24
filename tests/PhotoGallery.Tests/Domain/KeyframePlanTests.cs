using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// Where in a clip its stills are taken from.
/// </summary>
/// <remarks>
/// Worth pinning because every one of these positions costs a seek across the
/// share, and because the two edge cases - a clip too short to be worth three
/// frames, and one that will not say how long it is - are the ones a decoder
/// would otherwise discover the hard way.
/// </remarks>
public sealed class KeyframePlanTests
{
    [Fact]
    public void ANormalClipYieldsThreeFrames()
    {
        IReadOnlyList<TimeSpan> positions = KeyframePlan.PositionsFor(TimeSpan.FromMinutes(10));

        Assert.Equal(KeyframePlan.FrameCount, positions.Count);
    }

    [Fact]
    public void FramesAvoidTheVeryStartAndTheVeryEnd()
    {
        var length = TimeSpan.FromSeconds(100);

        IReadOnlyList<TimeSpan> positions = KeyframePlan.PositionsFor(length);

        // A fade in, a hand still moving, a black first frame: the moments most
        // likely to be worthless are exactly the two ends.
        Assert.True(positions[0] > TimeSpan.Zero);
        Assert.True(positions[^1] < length);
    }

    [Fact]
    public void FramesComeInOrder()
    {
        IReadOnlyList<TimeSpan> positions = KeyframePlan.PositionsFor(TimeSpan.FromMinutes(2));

        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void ThePosterIsTheFirstFrame()
    {
        IReadOnlyList<TimeSpan> positions = KeyframePlan.PositionsFor(TimeSpan.FromMinutes(2));

        // Ordinal 0 is the poster, so the frame most worth looking at should be
        // the one nearest the front rather than the middle of the clip.
        Assert.Equal(positions.Min(), positions[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AShortClipYieldsOneFrameAtTheStart(int seconds)
    {
        IReadOnlyList<TimeSpan> positions = KeyframePlan.PositionsFor(TimeSpan.FromSeconds(seconds));

        // Three seeks into a two second clip land on the same picture and cost
        // three decodes to learn one thing.
        Assert.Equal(new[] { TimeSpan.Zero }, positions);
    }

    [Fact]
    public void AnUnknownLengthStillYieldsItsFirstFrame()
    {
        IReadOnlyList<TimeSpan> positions = KeyframePlan.PositionsFor(null);

        // Some containers will not say how long they are. That is not a reason
        // to leave the clip without a poster.
        Assert.Equal(new[] { TimeSpan.Zero }, positions);
    }

    [Fact]
    public void ANonsensicalLengthIsTreatedAsUnknown()
    {
        IReadOnlyList<TimeSpan> positions = KeyframePlan.PositionsFor(TimeSpan.FromSeconds(-5));

        Assert.Equal(new[] { TimeSpan.Zero }, positions);
    }
}

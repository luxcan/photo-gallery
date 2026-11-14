using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// Which way up a playing clip is drawn: what its file asks for, plus the turn
/// the user saved.
/// </summary>
public sealed class PlaybackTurnTests
{
    private static readonly VideoOrientation UpsideDown = new(180, 1280, 720);
    private static readonly VideoOrientation Portrait = new(90, 1920, 1080);

    [Fact]
    public void AClipFiledUpsideDownIsTurnedRightOver()
    {
        // The case the shape guess could never see: the stream and the poster
        // are both landscape, so it left 141 clips playing upside down.
        Assert.Equal(180, PlaybackTurn.For(UpsideDown, 0, 1280, 720, stillIsPortrait: false));
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(90, 180)]
    [InlineData(180, 270)]
    [InlineData(270, 0)]
    [InlineData(-90, 0)]
    public void TheSavedTurnIsAddedToTheFiles(int saved, int expected)
    {
        Assert.Equal(expected, PlaybackTurn.For(Portrait, saved, 1920, 1080, stillIsPortrait: true));
    }

    [Fact]
    public void AStreamThePlayerHasAlreadyTurnedIsNotTurnedAgain()
    {
        // Stored 1920 by 1080 and reported 1080 by 1920: the player honoured the
        // note itself, and adding it again would put the clip on its side.
        Assert.Equal(0, PlaybackTurn.For(Portrait, 0, 1080, 1920, stillIsPortrait: true));
    }

    [Fact]
    public void TheSavedTurnStillCountsWhenThePlayerHasTurnedTheStream()
    {
        Assert.Equal(90, PlaybackTurn.For(Portrait, 90, 1080, 1920, stillIsPortrait: false));
    }

    [Fact]
    public void AStreamOfUnknownSizeTakesTheFileAtItsWord()
    {
        Assert.Equal(90, PlaybackTurn.For(Portrait, 0, 0, 0, stillIsPortrait: null));
    }

    [Fact]
    public void TheFileIsBelievedOverThePostersShape()
    {
        // The poster is only consulted when the file cannot say.
        Assert.Equal(180, PlaybackTurn.For(UpsideDown, 0, 1280, 720, stillIsPortrait: true));
    }

    [Fact]
    public void WithoutANotePortraitPosterOverLandscapeStreamIsAQuarterTurn()
    {
        // An .mts clip, or one too damaged to read: the shape guess it always was.
        Assert.Equal(90, PlaybackTurn.For(null, 0, 1920, 1080, stillIsPortrait: true));
    }

    [Fact]
    public void WithoutANoteMatchingShapesAreLeftAlone()
    {
        Assert.Equal(0, PlaybackTurn.For(null, 0, 1920, 1080, stillIsPortrait: false));
        Assert.Equal(0, PlaybackTurn.For(null, 0, 1080, 1920, stillIsPortrait: true));
    }

    [Fact]
    public void WithoutANoteTheSavedTurnIsNotCountedTwice()
    {
        // A landscape clip turned a quarter by the user. Its poster was turned
        // with it and is portrait now, so comparing against the poster as drawn
        // would add a quarter for the shape and another for the saved turn.
        Assert.Equal(90, PlaybackTurn.For(null, 90, 1920, 1080, stillIsPortrait: true));
    }

    [Fact]
    public void WithoutANoteAHalfTurnIsTheSavedTurnAlone()
    {
        Assert.Equal(180, PlaybackTurn.For(null, 180, 1920, 1080, stillIsPortrait: false));
    }

    [Fact]
    public void WithNothingToGoByOnlyTheSavedTurnIsDrawn()
    {
        Assert.Equal(270, PlaybackTurn.For(null, 270, 0, 0, stillIsPortrait: null));
        Assert.Equal(0, PlaybackTurn.For(null, 0, 1920, 1080, stillIsPortrait: null));
    }
}

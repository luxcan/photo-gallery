using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Finding the face another machine is talking about.
/// </summary>
/// <remarks>
/// Normally this is an exact match and none of it is interesting: both machines
/// ran the same detector over the same rendition and drew the same rectangles.
/// The fallback is for the day a GPU provider or a new model version moves every
/// box by a pixel - at which point an exact match finds nothing and every answer
/// in the library would be held for a face sitting right there.
/// </remarks>
public sealed class FaceMatchingTests
{
    private static readonly FaceBounds Head = new(100, 100, 60, 60);

    [Fact]
    public void TheSameBoxIsTheSameFace()
    {
        Assert.Equal(Head, FaceMatching.Find([Head], Head));
    }

    [Fact]
    public void ABoxThatMovedByAPixelIsTheSameFace()
    {
        FaceBounds nudged = new(101, 100, 60, 60);

        Assert.Equal(Head, FaceMatching.Find([Head], nudged));
    }

    [Fact]
    public void TheOtherFaceInThePhotographIsNotIt()
    {
        // Two people standing apart share no pixels at all, so there is nothing
        // near the threshold to be careful about.
        FaceBounds somebodyElse = new(400, 100, 60, 60);

        Assert.Null(FaceMatching.Find([somebodyElse], Head));
    }

    [Fact]
    public void AFaceThisMachineHasNotFoundIsNoMatchRatherThanTheNearestOne()
    {
        // Which is what makes the answer wait instead of landing on the wrong
        // person. Half overlap is a long way from a plausible second face.
        FaceBounds halfOver = new(140, 100, 60, 60);

        Assert.Null(FaceMatching.Find([halfOver], Head));
    }

    [Fact]
    public void TheBestMatchWinsRatherThanTheFirstOneCloseEnough()
    {
        FaceBounds nearly = new(112, 100, 60, 60);
        FaceBounds better = new(102, 100, 60, 60);

        Assert.Equal(better, FaceMatching.Find([nearly, better], Head));
        Assert.Equal(better, FaceMatching.Find([better, nearly], Head));
    }

    [Fact]
    public void APhotographWithNoFacesFoundYetMatchesNothing()
    {
        Assert.Null(FaceMatching.Find([], Head));
    }

    [Theory]
    [InlineData(100, 100, 1.0)]
    [InlineData(160, 100, 0.0)]
    [InlineData(400, 400, 0.0)]
    public void OverlapIsIntersectionOverUnion(int x, int y, double expected)
    {
        Assert.Equal(expected, FaceMatching.Overlap(Head, new FaceBounds(x, y, 60, 60)), 3);
    }
}

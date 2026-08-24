using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// Measured on the real library: one photograph exists as eight files, byte for
/// byte identical, named minutes apart. Each carries its own six faces, so a
/// review grid showing every copy asks the same question eight times over.
/// </summary>
public sealed class FaceOnPictureTests
{
    private static readonly FaceBounds s_face = new(10, 20, 60, 60);

    [Fact]
    public void Comparer_TreatsCopiesOfOnePhotographAsOne()
    {
        Assert.True(FaceOnPicture.Comparer.Equals(("a3f1.jpg", s_face), ("a3f1.jpg", s_face)));
        Assert.Equal(
            FaceOnPicture.Comparer.GetHashCode(("a3f1.jpg", s_face)),
            FaceOnPicture.Comparer.GetHashCode(("a3f1.jpg", s_face)));
    }

    [Fact]
    public void Comparer_IgnoresHowTheRenditionWasSpelled()
    {
        Assert.True(FaceOnPicture.Comparer.Equals(("a3f1.jpg", s_face), ("A3F1.JPG", s_face)));
        Assert.Equal(
            FaceOnPicture.Comparer.GetHashCode(("a3f1.jpg", s_face)),
            FaceOnPicture.Comparer.GetHashCode(("A3F1.JPG", s_face)));
    }

    [Fact]
    public void Comparer_KeepsTwoPeopleInOnePictureApart()
    {
        // Two faces in the same photograph are two questions, not one - which is
        // the whole difference between this and simply grouping by picture.
        var otherPerson = new FaceBounds(90, 20, 60, 60);

        Assert.False(FaceOnPicture.Comparer.Equals(("a3f1.jpg", s_face), ("a3f1.jpg", otherPerson)));
    }

    [Fact]
    public void Comparer_KeepsTheSameFaceInDifferentPicturesApart()
    {
        Assert.False(FaceOnPicture.Comparer.Equals(("a3f1.jpg", s_face), ("b2e0.jpg", s_face)));
    }
}

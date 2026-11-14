using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The crop the landmark graph is shown. Closed form, so the answers are
/// arithmetic: a crop that is slightly off still yields 106 plausible points,
/// just not on the eyes.
/// </summary>
public sealed class FaceLandmarkerTests
{
    private static readonly FaceBounds s_box = new(100, 200, 80, 120);

    [Fact]
    public void CropFor_PutsTheBoxCentreInTheMiddleOfTheCrop()
    {
        (float x, float y) = FaceLandmarker.CropFor(s_box, roll: 0f).Apply(140f, 260f);

        Assert.Equal(FaceLandmarker.InputEdge / 2f, x, 3);
        Assert.Equal(FaceLandmarker.InputEdge / 2f, y, 3);
    }

    [Fact]
    public void CropFor_FitsTheLongerSideWithHalfAgainAsMargin()
    {
        // 120px tall, so 180px of picture spans the 192px crop.
        SimilarityTransform transform = FaceLandmarker.CropFor(s_box, roll: 0f);

        Assert.Equal(FaceLandmarker.InputEdge / 180f, transform.Scale, 5);
        Assert.Equal(0f, transform.B, 6);
    }

    [Fact]
    public void CropFor_TurnsAFaceOnItsSideLevel()
    {
        // The picture-left eye directly above the picture-right one: a quarter
        // turn clockwise. After the crop transform they must sit side by side at
        // one height, the picture-left eye still on the left.
        SimilarityTransform transform = FaceLandmarker.CropFor(s_box, roll: MathF.PI / 2f);

        (float leftX, float leftY) = transform.Apply(140f, 240f);
        (float rightX, float rightY) = transform.Apply(140f, 280f);

        Assert.Equal(leftY, rightY, 3);
        Assert.True(leftX < rightX);
        Assert.Equal(FaceLandmarker.InputEdge / 180f, transform.Scale, 5);
    }

    [Fact]
    public void Warp_OfTheIdentityIsTheSamePicture()
    {
        // The sampler the landmark crop and the recognition crop now share.
        byte[] pixels = new byte[4 * 3 * BgrImage.BytesPerPixel];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i * 7);
        }

        var image = new BgrImage(pixels, 4, 3);

        BgrImage warped = FaceAligner.Warp(image, new SimilarityTransform(1f, 0f, 0f, 0f), 4, 3)!;

        Assert.Equal(pixels, warped.Pixels);
    }
}

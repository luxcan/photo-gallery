using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.App.Imaging;
using PhotoGallery.App.People;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Tests.App;

/// <summary>
/// A face outline that does not follow the picture is worse than no outline at
/// all - it points confidently at the wrong person - so the arithmetic that
/// places it is pinned here.
/// </summary>
public sealed class PictureFitTests
{
    [Fact]
    public void Of_HasNothingToPlaceAgainstBeforeThePictureArrives() =>
        Assert.Null(PictureFit.Of(800, 600, null));

    [Fact]
    public void Of_HasNothingToPlaceAgainstInAnAreaWithNoRoom()
    {
        BitmapSource picture = Picture(200, 100);

        Assert.Null(PictureFit.Of(0, 600, picture));
        Assert.Null(PictureFit.Of(800, 0, picture));
    }

    [Fact]
    public void Of_FitsTheEdgeThatRunsOutFirstAndCentresTheRest()
    {
        // A 200x100 picture in a 400x400 area: width doubles to fill, and the
        // 200 points of height left over are split above and below.
        PictureFit? fit = PictureFit.Of(400, 400, Picture(200, 100));

        Assert.NotNull(fit);
        Assert.Equal(2d, fit!.Value.Scale, 6);
        Assert.Equal(0d, fit.Value.OffsetX, 6);
        Assert.Equal(100d, fit.Value.OffsetY, 6);
    }

    [Fact]
    public void PlaceWithin_PutsTheBoxOverTheFaceWhereverThePictureIsDrawn()
    {
        var face = new FaceCropItem(Thumbnail(new FaceBounds(100, 20, 40, 60)));

        face.PlaceWithin(PictureFit.Of(400, 400, Picture(200, 100))!.Value);

        Assert.Equal(200d, face.Left, 6);
        Assert.Equal(140d, face.Top, 6);
        Assert.Equal(80d, face.Width, 6);
        Assert.Equal(120d, face.Height, 6);
    }

    [Fact]
    public void PlaceWithin_KeepsTheBoxOnTheFaceWhenTheWindowChangesSize()
    {
        // The same face, the same picture, half the room. Every number halves
        // with it - which is the whole reason the boxes are laid out again on
        // every resize rather than worked out once.
        var large = new FaceCropItem(Thumbnail(new FaceBounds(100, 20, 40, 60)));
        var small = new FaceCropItem(Thumbnail(new FaceBounds(100, 20, 40, 60)));

        large.PlaceWithin(PictureFit.Of(400, 400, Picture(200, 100))!.Value);
        small.PlaceWithin(PictureFit.Of(200, 200, Picture(200, 100))!.Value);

        Assert.Equal(large.Left / 2d, small.Left, 6);
        Assert.Equal(large.Top / 2d, small.Top, 6);
        Assert.Equal(large.Width / 2d, small.Width, 6);
        Assert.Equal(large.Height / 2d, small.Height, 6);
    }

    [Fact]
    public void Caption_SaysWhenSoAChildCanBeJudgedByTheirAge()
    {
        var face = new FaceCropItem(Thumbnail(new FaceBounds(0, 0, 10, 10)));

        Assert.Contains("2018", face.Caption, StringComparison.Ordinal);
    }

    [Fact]
    public void FileNameAndFolder_NameThePictureTheWayTheViewerDoes()
    {
        // A crop with a date on it is still an anonymous face. The file is what
        // lets the same picture be recognised, or found outside this app.
        var face = new FaceCropItem(Thumbnail(new FaceBounds(0, 0, 10, 10)));

        Assert.Equal("IMG_1234.jpg", face.FileName);
        Assert.Equal(@"2018 birthday\day two", face.FolderPath);
    }

    private static FaceThumbnail Thumbnail(FaceBounds bounds) =>
        new(1, 7, "aa.jpg", bounds,
            new DateTime(2018, 5, 4, 0, 0, 0, DateTimeKind.Utc),
            @"2018 birthday\day two\IMG_1234.jpg",
            @"\\nas\photos\2018 birthday\day two\IMG_1234.jpg");

    private static BitmapSource Picture(int width, int height) => BitmapSource.Create(
        width, height, 96, 96, PixelFormats.Gray8, null, new byte[width * height], width);
}

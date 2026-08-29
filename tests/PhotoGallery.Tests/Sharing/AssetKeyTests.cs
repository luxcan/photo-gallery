using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// What one photograph is called on two machines.
/// </summary>
/// <remarks>
/// The key is the path below a matched source, and everything else in sharing
/// rests on two machines agreeing about it. The cases here are the ones where
/// the same file is written down two ways: a separator, a capital letter, and
/// the two sources being different folders that happen to hold the same name.
/// </remarks>
public sealed class AssetKeyTests
{
    private static readonly Guid Share = Guid.Parse("1f5d0a2b-0000-4000-8000-000000000001");

    [Fact]
    public void TheSameFileWrittenWithEitherSeparatorIsOneKey()
    {
        Assert.Equal(
            new AssetKey(Share, @"2019\Genting\IMG_01.JPG"),
            new AssetKey(Share, "2019/Genting/IMG_01.JPG"));
    }

    [Fact]
    public void CaseIsNotAFactAboutWhichPhotographThisIs()
    {
        // Windows opens both of these and gets the same file, so a key that
        // disagreed would hold two answers about one picture and match neither.
        Assert.Equal(
            new AssetKey(Share, @"2019\img_01.jpg"),
            new AssetKey(Share, @"2019\IMG_01.JPG"));
    }

    [Fact]
    public void KeysThatMatchHashTheSame()
    {
        // Every lookup in the merge is a dictionary lookup, so equality without
        // this would be equality nothing ever reaches.
        var index = new Dictionary<AssetKey, string>
        {
            [new AssetKey(Share, "2019/img_01.jpg")] = "found",
        };

        Assert.Equal("found", index[new AssetKey(Share, @"2019\IMG_01.JPG")]);
    }

    [Fact]
    public void TheSamePathUnderTwoSourcesIsTwoPhotographs()
    {
        // The reason a key carries the source at all. Every phone dump has an
        // IMG_0001.JPG in it.
        Assert.NotEqual(
            new AssetKey(Share, "IMG_0001.JPG"),
            new AssetKey(Guid.NewGuid(), "IMG_0001.JPG"));
    }

    [Fact]
    public void ALeadingSeparatorIsNotPartOfTheName()
    {
        Assert.Equal(
            new AssetKey(Share, "Genting/IMG_01.JPG"),
            new AssetKey(Share, @"\Genting\IMG_01.JPG"));
    }

    [Fact]
    public void APathIsRequired()
    {
        // A key with no path is a decision about no photograph, which would match
        // whatever else was written down the same way.
        Assert.Throws<ArgumentException>(() => new AssetKey(Share, "  "));
    }
}

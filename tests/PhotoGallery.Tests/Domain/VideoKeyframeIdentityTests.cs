using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// What a video's frames are called, and when that name is allowed to change.
/// </summary>
/// <remarks>
/// The whole of the keyframe pass's resumability rests on this. A name that
/// wandered between runs would leave a second set of frames beside the first and
/// have the pass seek through 267 GB again; a name that refused to change when
/// the file did would leave the frames of a video nobody has any more.
/// </remarks>
public sealed class VideoKeyframeIdentityTests
{
    private static readonly DateTime s_modified = new(2026, 3, 5, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void TheSameFileResolvesToTheSameName()
    {
        string first = VideoKeyframeIdentity.For(@"2023\clip.mp4", 5_000, s_modified, 0);
        string second = VideoKeyframeIdentity.For(@"2023\clip.mp4", 5_000, s_modified, 0);

        Assert.Equal(first, second);
    }

    [Fact]
    public void EachFrameOfAClipGetsItsOwnName()
    {
        string[] names =
        [
            VideoKeyframeIdentity.For(@"2023\clip.mp4", 5_000, s_modified, 0),
            VideoKeyframeIdentity.For(@"2023\clip.mp4", 5_000, s_modified, 1),
            VideoKeyframeIdentity.For(@"2023\clip.mp4", 5_000, s_modified, 2),
        ];

        Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AFileThatGrewIsRebuilt()
    {
        string before = VideoKeyframeIdentity.For(@"2023\clip.mp4", 5_000, s_modified, 0);
        string after = VideoKeyframeIdentity.For(@"2023\clip.mp4", 6_000, s_modified, 0);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void AFileTouchedSinceIsRebuilt()
    {
        string before = VideoKeyframeIdentity.For(@"2023\clip.mp4", 5_000, s_modified, 0);
        string after = VideoKeyframeIdentity.For(
            @"2023\clip.mp4", 5_000, s_modified.AddSeconds(1), 0);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void TwoDifferentFilesDoNotCollide()
    {
        string one = VideoKeyframeIdentity.For(@"2023\clip.mp4", 5_000, s_modified, 0);
        string other = VideoKeyframeIdentity.For(@"2023\other.mp4", 5_000, s_modified, 0);

        Assert.NotEqual(one, other);
    }

    [Fact]
    public void TheSameFileReachedInDifferentCaseIsTheSameFile()
    {
        string lower = VideoKeyframeIdentity.For(@"2023\clip.mp4", 5_000, s_modified, 0);
        string upper = VideoKeyframeIdentity.For(@"2023\CLIP.MP4", 5_000, s_modified, 0);

        // Windows paths differ in case and mean one file. Rebuilding a clip's
        // frames because a share was remounted under a different case would cost
        // a seek per video for nothing.
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void TheNameIsLongEnoughForTheStoreToShardIt()
    {
        string name = VideoKeyframeIdentity.For(@"2023\clip.mp4", 5_000, s_modified, 0);

        // The store takes the first 32 characters and shards on the first two,
        // so anything shorter would collide or fail to spread.
        Assert.Equal(64, name.Length);
        Assert.True(name.All(Uri.IsHexDigit));
    }
}

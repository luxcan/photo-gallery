using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// Grouping photographs that look alike. Hashes are written as bit patterns so
/// every expected distance is countable rather than a guess.
/// </summary>
public sealed class NearDuplicatesTests
{
    [Fact]
    public void Group_PutsPicturesWithinTheThresholdTogether()
    {
        // Two bits apart.
        IReadOnlyList<IReadOnlyList<Asset>> sets = NearDuplicates.Group(
            [Photo(1, 0b0000), Photo(2, 0b0011), Photo(3, 0b1111_1111)], threshold: 4);

        IReadOnlyList<Asset> set = Assert.Single(sets);
        Assert.Equal([1, 2], set.Select(asset => asset.Id));
    }

    [Fact]
    public void Group_LeavesAPictureAloneWhenNothingIsCloseToIt()
    {
        Assert.Empty(NearDuplicates.Group([Photo(1, 0b0000), Photo(2, 0b1111_1111)]));
    }

    [Fact]
    public void Group_DoesNotChainOneNearMatchIntoAnother()
    {
        // 0, 4 and 8 bits: the first and last are eight apart and are not the
        // same picture. Transitive closure would drag them into one set, and a
        // set the user cannot see the sense of is worse than no set.
        IReadOnlyList<IReadOnlyList<Asset>> sets = NearDuplicates.Group(
            [Photo(1, 0b0000_0000), Photo(2, 0b0000_1111), Photo(3, 0b1111_1111)],
            threshold: 4);

        IReadOnlyList<Asset> set = Assert.Single(sets);
        Assert.Equal([1, 2], set.Select(asset => asset.Id));
    }

    [Fact]
    public void Group_GivesTheSameAnswerWhateverOrderItIsHandedThem()
    {
        // Leader clustering depends on who goes first, so the order is fixed by
        // id. A pass whose answers moved between runs could not be trusted.
        Asset[] photos = [Photo(1, 0b0000), Photo(2, 0b0001), Photo(3, 0b0011)];

        Assert.Equal(
            NearDuplicates.Group(photos).Select(set => set.Select(a => a.Id)),
            NearDuplicates.Group(photos.Reverse()).Select(set => set.Select(a => a.Id)));
    }

    [Fact]
    public void Group_IgnoresPicturesThatWereNeverHashed()
    {
        // A photo that has not been prepared has nothing to compare, and a
        // missing hash must not read as a hash of zero - that would group every
        // unprepared photo in the library together.
        var unprepared = new Asset { RelativePath = "a.jpg", Id = 2 };

        Assert.Empty(NearDuplicates.Group([Photo(1, 0), unprepared]));
    }

    [Fact]
    public void DistanceFrom_CountsTheBitsThatDiffer()
    {
        Assert.Equal(4, NearDuplicates.DistanceFrom(Photo(1, 0b0000), Photo(2, 0b1111)));
        Assert.Equal(0, NearDuplicates.DistanceFrom(Photo(1, 0b1010), Photo(2, 0b1010)));
    }

    private static Asset Photo(int id, ulong hash) => new()
    {
        Id = id,
        RelativePath = $"20230201\\photo-{id}.jpg",
        PerceptualHash = new PerceptualHash(hash),
    };
}

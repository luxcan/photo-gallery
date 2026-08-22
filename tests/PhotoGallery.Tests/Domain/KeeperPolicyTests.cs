using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Tests.Domain;

public sealed class KeeperPolicyTests
{
    [Theory]
    [InlineData("20230201", true)]        // bare month-day date
    [InlineData("202302", true)]          // bare month
    [InlineData("20230203 - Chingay", false)]
    [InlineData("20200214_Ana Lim Born", false)]
    [InlineData("Randoms", false)]
    [InlineData("", false)]
    [InlineData("2023", false)]           // too short to be a dated folder here
    [InlineData("202302031", false)]      // too long
    public void IsGenericFolder_RecognisesBareDates(string folder, bool expected) =>
        Assert.Equal(expected, KeeperPolicy.IsGenericFolder(folder));

    [Fact]
    public void ChooseKeeper_PrefersNamedEventOverGenericMonth()
    {
        // The real case that was originally decided backwards: the same video in
        // a catch-all month folder and in the named event folder.
        Asset generic = AssetAt(@"20230201\IMG_6769.MOV");
        Asset named = AssetAt(@"20230203 - Chingay\OYAD4509.MOV");

        Asset keeper = KeeperPolicy.ChooseKeeper([generic, named]);

        Assert.Same(named, keeper);
    }

    [Fact]
    public void ChooseKeeper_PrefersShallowerPathWhenBothNamed()
    {
        Asset shallow = AssetAt(@"20160909 - Coast trip\P1010001.JPG");
        Asset deep = AssetAt(@"20160909 - Coast trip\edits\P1010001.JPG");

        Assert.Same(shallow, KeeperPolicy.ChooseKeeper([deep, shallow]));
    }

    [Fact]
    public void ChooseKeeper_IsStableRegardlessOfInputOrder()
    {
        Asset a = AssetAt(@"20230201\a.jpg");
        Asset b = AssetAt(@"20230201\b.jpg");

        Assert.Same(a, KeeperPolicy.ChooseKeeper([a, b]));
        Assert.Same(a, KeeperPolicy.ChooseKeeper([b, a]));
    }

    [Fact]
    public void ChooseKeeper_EmptySetThrows() =>
        Assert.Throws<ArgumentException>(() => KeeperPolicy.ChooseKeeper([]));

    [Fact]
    public void AssignRoles_MarksExactlyOneKeeper()
    {
        var set = new DuplicateSet { Kind = DuplicateKind.Exact };
        set.Members.Add(MemberFor(AssetAt(@"20230201\IMG_6769.MOV")));
        set.Members.Add(MemberFor(AssetAt(@"20230203 - Chingay\OYAD4509.MOV")));
        set.Members.Add(MemberFor(AssetAt(@"20230201\copy\IMG_6769.MOV")));

        set.AssignRoles();

        Assert.Equal(1, set.Members.Count(m => m.Role == DuplicateRole.Keeper));
        DuplicateMember keeper = set.Members.Single(m => m.Role == DuplicateRole.Keeper);
        Assert.Equal(@"20230203 - Chingay\OYAD4509.MOV", keeper.Asset!.RelativePath);
    }

    private static Asset AssetAt(string relativePath) => new()
    {
        RelativePath = relativePath,
        Length = 1024,
    };

    private static DuplicateMember MemberFor(Asset asset) => new()
    {
        Asset = asset,
    };
}

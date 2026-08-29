using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A decision set surviving being written down and read back.
/// </summary>
/// <remarks>
/// The one place in this feature where being wrong is silent. A key that does
/// not round-trip makes every answer in the file miss, and a vector rounded on
/// the way through does not fail either - it answers confidently about the wrong
/// person. Both look exactly like a library that has simply not been told
/// anything.
/// </remarks>
public sealed class DecisionSetFileTests
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Ana = new("a0000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task EverythingComesBackTheWayItWentIn()
    {
        Guid album = Guid.NewGuid();
        AssetKey photo = Pictures.Photo(@"2019 Genting\IMG_6769.JPG");
        FaceKey face = new(photo, new FaceBounds(12, 34, 56, 78));

        DecisionSet before = new Machine("Mum")
            .Knows(new SharedPerson(Ana, "Ana Lim", 2020, Monday, null))
            .Confirms(face, Ana, Monday)
            .CallsNobody(new FaceKey(photo, new FaceBounds(300, 40, 50, 50)), Monday)
            .Turns(photo, 270, Monday)
            .HasAlbum(Pictures.Album(album, "Genting Trip", Monday))
            .Puts(photo, album, Monday)
            .Refuses(photo, "2019-03-03..2019-03-05", Monday)
            .Remembers(Ana, Monday, Monday.AddYears(1), degrees: 37)
            .Set();

        DecisionSet after = await RoundTripAsync(before);

        Assert.Equal(before.Machine, after.Machine);
        Assert.Equal(before.Sources, after.Sources);
        Assert.Equal(before.People, after.People);
        Assert.Equal(before.Answers, after.Answers);
        Assert.Equal(before.Strangers, after.Strangers);
        Assert.Equal(before.Turns, after.Turns);
        Assert.Equal(before.Albums, after.Albums);
        Assert.Equal(before.Memberships, after.Memberships);
        Assert.Equal(before.Rejections, after.Rejections);
    }

    [Fact]
    public async Task AFaceVectorComesBackExactly()
    {
        // Not approximately. An embedding is compared by dot product against
        // every other one in the library, and a value that drifted in the fourth
        // decimal place still returns an answer.
        FaceEmbedding centroid = TestEmbeddings.At(37);

        DecisionSet after = await RoundTripAsync(
            new Machine("Mum").Remembers(Ana, Monday, Monday.AddYears(1), degrees: 37).Set());

        SharedEra era = Assert.Single(after.Eras);

        Assert.Equal(1f, era.Centroid.SimilarityTo(centroid), 6);
        Assert.True(centroid.Values.SequenceEqual(era.Centroid.Values));
    }

    [Fact]
    public async Task APathWithSpacesAndPunctuationSurvives()
    {
        // Folder names in this library carry real meaning and real punctuation:
        // "20230203 - Chingay", "20200214_Ana Lim Born".
        AssetKey awkward = Pictures.Photo(@"20230203 - Chingay\IMG_6769 (1).MOV");

        DecisionSet after = await RoundTripAsync(
            new Machine("Mum").Turns(awkward, 90, Monday).Set());

        Assert.Equal(awkward, Assert.Single(after.Turns).Photo);
    }

    [Fact]
    public async Task ItIsSmallerThanTheJsonItHolds()
    {
        // Most of the file is the same few thousand path strings repeated, which
        // is exactly what a compressor is for.
        DecisionSet many = Crowded();

        using var stream = new MemoryStream();
        await DecisionSetFile.WriteAsync(stream, many);

        int uncompressed = System.Text.Json.JsonSerializer
            .Serialize(many.Answers.Select(a => a.Face.ToString())).Length;

        Assert.True(
            stream.Length < uncompressed,
            $"gzipped {stream.Length} bytes against {uncompressed} of keys alone");
    }

    [Fact]
    public async Task SomethingThatIsNotADecisionSetIsRefusedRatherThanGuessedAt()
    {
        using var rubbish = new MemoryStream("not a gzip file"u8.ToArray());

        await Assert.ThrowsAnyAsync<Exception>(() => DecisionSetFile.ReadAsync(rubbish));
    }

    private static DecisionSet Crowded()
    {
        var mum = new Machine("Mum");

        for (int i = 0; i < 500; i++)
        {
            mum.Confirms(Pictures.Face($@"2019 Genting\IMG_{i:D4}.JPG"), Ana, Monday);
        }

        return mum.Knows(new SharedPerson(Ana, "Ana", null, null, null)).Set();
    }

    private static async Task<DecisionSet> RoundTripAsync(DecisionSet before)
    {
        using var stream = new MemoryStream();
        await DecisionSetFile.WriteAsync(stream, before);

        stream.Position = 0;
        return await DecisionSetFile.ReadAsync(stream);
    }
}

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// What the sync folder hands this library, when somebody other than the app
/// put it there.
/// </summary>
/// <remarks>
/// Anybody who can write to the folder can leave a file in it, and every scan
/// and every Sync reads what is there. Each case here once closed the app or
/// put a stranger's picture on screen; each must now be one file not taken,
/// with every good file beside it still taken.
/// </remarks>
public sealed class SyncFolderInputTests : IDisposable
{
    private static readonly MachineIdentity Elsewhere =
        new(new Guid("e0000000-0000-4000-8000-000000000001"), "Another laptop", "1.0.0", 1);

    /// <summary>Stands for a name one character longer than the library keeps.</summary>
    private const string TooLong = "@long";

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    // ------------------------------------------------------- files with holes

    [Fact]
    public async Task AnEmptyAnswersFileIsOneFileNotReadRatherThanTheAppClosing()
    {
        Mum.Photo(@"2019\a.jpg");
        Dad.Photo(@"2019\a.jpg");
        await Mum.Publishing.HandleAsync();

        await PlantAsync(SharedFolderExchange.AnswersFolder, DecisionSetFile.Extension, "{}");

        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.True(merged.Merged);
        Assert.Contains("missing", Assert.Single(merged.Unreadable).Problem, StringComparison.Ordinal);
    }

    /// <summary>Each hole a file can have, read the way the folder holds it.</summary>
    [Theory]
    [InlineData("""{"Machine":null}""")]
    [InlineData("""{"Machine":{"Id":"00000000-0000-0000-0000-000000000000","Name":"x","AppVersion":"1","SchemaVersion":1}}""")]
    [InlineData("""{"Albums":[null]}""")]
    [InlineData("""{"Albums":[{"PublicId":"a0000000-0000-4000-8000-000000000001"}]}""")]
    [InlineData("""{"Answers":[{"Person":"a0000000-0000-4000-8000-000000000001"}]}""")]
    [InlineData("""{"Eras":[{"Person":"a0000000-0000-4000-8000-000000000001","SampleCount":3}]}""")]
    [InlineData("""{"Collections":null}""")]
    [InlineData("""{"DeletedSuggestions":null}""")]
    [InlineData("""{"People":[{"PublicId":"a0000000-0000-4000-8000-000000000001","DisplayName":"@long"}]}""")]
    public async Task AnswersWithAHoleInThemAreRefusedAsUnreadable(string hole)
    {
        string json = Whole(hole.Replace(TooLong, new string('x', 129), StringComparison.Ordinal));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => DecisionSetFile.ReadAsync(Gzipped(json)));
    }

    [Fact]
    public async Task AWholeAnswersFileStillReads()
    {
        DecisionSet read = await DecisionSetFile.ReadAsync(Gzipped(Whole("{}")));

        Assert.Equal(Elsewhere.Id, read.Machine.Id);
    }

    [Fact]
    public async Task ANameAsLongAsTheLibraryKeepsStillReads()
    {
        string name = new('x', 128);

        DecisionSet read = await DecisionSetFile.ReadAsync(Gzipped(Whole(
            $$"""{"People":[{"PublicId":"a0000000-0000-4000-8000-000000000001","DisplayName":"{{name}}"}]}""")));

        Assert.Equal(name, Assert.Single(read.People).DisplayName);
    }

    /// <summary>
    /// The one name repaired rather than refused: a computer must stay one
    /// somebody can turn down on the Sync screen, whatever it calls itself.
    /// </summary>
    [Theory]
    [InlineData("\"@long\"")]
    [InlineData("null")]
    [InlineData("\"   \"")]
    public async Task AComputerNameTooLongOrMissingIsRepairedNotRefused(string name)
    {
        string said = name.Replace(TooLong, new string('x', 400), StringComparison.Ordinal);
        string machine = $$$"""{"Machine":{"Id":"e0000000-0000-4000-8000-000000000001","Name":{{{said}}},"AppVersion":"1","SchemaVersion":1}}""";

        DecisionSet read = await DecisionSetFile.ReadAsync(Gzipped(Whole(machine)));

        Assert.InRange(read.Machine.Name.Length, 1, KnownMachine.NameLength);
        Assert.False(string.IsNullOrWhiteSpace(read.Machine.Name));
    }

    [Fact]
    public async Task AFacesFileFromAComputerWithAnOverlongNameIsReadWithTheNameCut()
    {
        await Mum.Pool.PublishFacesAsync(
            FaceSet.Empty(Elsewhere with { Name = new string('x', 400) }, DateTime.UtcNow));

        FaceSet read = Assert.Single(await Dad.Pool.FetchFacesAsync());

        Assert.Equal(KnownMachine.NameLength, read.Machine.Name.Length);
    }

    [Fact]
    public async Task AnEmptyFacesFileIsSkippedAndTheGoodOnesAreStillRead()
    {
        await PlantAsync(SharedFolderPool.VectorsFolder, SharedFolderPool.VectorsExtension, "{}");
        await Mum.Pool.PublishFacesAsync(FaceSet.Empty(Elsewhere, DateTime.UtcNow));

        Assert.Equal(Elsewhere.Id, Assert.Single(await Dad.Pool.FetchFacesAsync()).Machine.Id);
    }

    [Fact]
    public async Task AnEmptyFactsFileIsSkippedAndTheGoodOnesAreStillTaken()
    {
        await PlantAsync(SharedFolderPool.FactsFolder, SharedFolderPool.Extension, "{}");
        await PlantAsync(
            SharedFolderPool.FactsFolder,
            SharedFolderPool.Extension,
            """{"Machine":{"Id":"e0000000-0000-4000-8000-000000000002","Name":"x","AppVersion":"1","SchemaVersion":1},"Facts":[{"Length":1}],"Models":{}}""");

        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Photo(@"2019\a.jpg");
        await Mum.Pooling.GiveAsync();

        Assert.Equal(Mum.MachineId, Assert.Single(await Dad.Pool.FetchAsync()).Machine.Id);

        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.True(taken.Ran, taken.Problem);
        Assert.Equal(1, taken.Filled);
    }

    [Theory]
    [InlineData("""{"Machine":null}""")]
    [InlineData("""{"Models":{},"Faces":null}""")]
    [InlineData("""{"Models":{"faces":null},"Faces":[]}""")]
    [InlineData("""{"Models":{},"Faces":[null]}""")]
    [InlineData("""{"Models":{},"Faces":[{"Face":"a0000000-0000-4000-8000-000000000001:2019\\a.jpg|1,1,10,10","DetectScore":0.9}]}""")]
    public async Task AFacesFileWithAHoleIsSkippedAndTheGoodOnesAreStillRead(string hole)
    {
        await PlantAsync(SharedFolderPool.VectorsFolder, SharedFolderPool.VectorsExtension, Whole(hole));
        await Mum.Pool.PublishFacesAsync(FaceSet.Empty(Elsewhere, DateTime.UtcNow));

        FaceSet read = Assert.Single(await Dad.Pool.FetchFacesAsync());

        Assert.Equal(Elsewhere.Id, read.Machine.Id);
    }

    // ------------------------------------------------------------ size limits

    [Fact]
    public async Task AFileThatUnpacksPastItsLimitIsRefused()
    {
        string json = Whole($$"""{"Padding":"{{new string('a', 4096)}}"}""");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => SharedFolderFile.ReadAsync<DecisionSet>(Gzipped(json), 1024, CancellationToken.None));
    }

    [Fact]
    public async Task AFileExactlyAtItsLimitStillReads()
    {
        string json = Whole("{}");
        int length = Encoding.UTF8.GetByteCount(json);

        DecisionSet read = await SharedFolderFile.ReadAsync<DecisionSet>(
            Gzipped(json), length, CancellationToken.None);

        Assert.Equal(Elsewhere.Id, read.Machine.Id);
    }

    [Fact]
    public async Task APackedFileBiggerThanItsLimitIsRefusedUnopened()
    {
        using var big = new MemoryStream(new byte[2048]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => SharedFolderFile.ReadAsync<DecisionSet>(big, 1024, CancellationToken.None));

        Assert.Equal(0, big.Position);
    }

    /// <summary>
    /// A few hundred kilobytes that unpack to more than any library writes:
    /// once a way to run the app out of memory on every scan.
    /// </summary>
    [Fact]
    public async Task ADecompressionBombInTheFolderIsOneFileNotRead()
    {
        Mum.Photo(@"2019\a.jpg");
        Dad.Photo(@"2019\a.jpg");
        await Mum.Publishing.HandleAsync();

        string answers = Path.Combine(_house.SharedFolder, SharedFolderExchange.AnswersFolder);

        await using (var file = File.Create(Path.Combine(answers, $"{Guid.NewGuid():D}{DecisionSetFile.Extension}")))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            byte[] spaces = new byte[1024 * 1024];
            Array.Fill(spaces, (byte)' ');

            await gzip.WriteAsync("{"u8.ToArray());

            for (long written = 0; written <= SharedFolderFile.AnswersLimit; written += spaces.Length)
            {
                await gzip.WriteAsync(spaces);
            }

            await gzip.WriteAsync("}"u8.ToArray());
        }

        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.True(merged.Merged);
        Assert.Contains("MB", Assert.Single(merged.Unreadable).Problem, StringComparison.Ordinal);
    }

    // ----------------------------------------------------- pictures by name

    [Fact]
    public async Task APooledFileThatIsNotAPictureIsNotTaken()
    {
        await GiveOneAsync();
        await File.WriteAllTextAsync(PooledPreview(), "not a picture at all");

        await AssertNotTakenAsync();
    }

    [Fact]
    public async Task APooledPictureBiggerThanAnyTileIsNotTaken()
    {
        // A picture, and a JPEG, but far wider than any tile this app makes:
        // the kind of file that costs the grid its memory when it is opened.
        await GiveOneAsync();
        await File.WriteAllBytesAsync(PooledTile(), TestJpeg.Bytes(2000, 10));

        await AssertNotTakenAsync();
    }

    [Fact]
    public async Task APooledPictureHeavierThanAnyPreviewIsNotTaken()
    {
        await GiveOneAsync();

        byte[] padded = [.. Library.PreviewBytes, .. new byte[5 * 1024 * 1024]];
        await File.WriteAllBytesAsync(PooledPreview(), padded);

        await AssertNotTakenAsync();
    }

    [Fact]
    public async Task OneBadHalfMeansNeitherHalfIsCopied()
    {
        // The preview is good and would be copied first; the tile is not. A
        // pair is judged whole before anything of it lands here.
        await GiveOneAsync();
        await File.WriteAllTextAsync(PooledTile(), "not a picture at all");

        await AssertNotTakenAsync();

        Assert.False(File.Exists(Dad.Thumbnails.ResolvePreviewPath("aa11.jpg")));
    }

    [Fact]
    public async Task AGoodPooledPictureIsStillTaken()
    {
        await GiveOneAsync();

        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(1, taken.Fetched);
        Assert.Equal(Library.TileBytes, await File.ReadAllBytesAsync(Dad.Thumbnails.ResolveTilePath("aa11.jpg")));
        Assert.Equal(
            Library.PreviewBytes, await File.ReadAllBytesAsync(Dad.Thumbnails.ResolvePreviewPath("aa11.jpg")));
    }

    // ------------------------------------------------------------------ setup

    private async Task GiveOneAsync()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Photo(@"2019\a.jpg");
        await Mum.Pooling.GiveAsync();
    }

    /// <summary>
    /// The photograph is left for this computer to prepare, as though the
    /// folder had never had it.
    /// </summary>
    private async Task AssertNotTakenAsync()
    {
        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.True(taken.Ran, taken.Problem);
        Assert.Equal(0, taken.Fetched);
        Assert.Equal(0, taken.Filled);
        Assert.False(Dad.Holds("aa11.jpg"));
        Assert.Null(Dad.Db.Assets.AsNoTracking().Single().ThumbnailName);
    }

    private string PooledTile() =>
        Path.Combine(_house.SharedFolder, SharedFolderPool.ThumbsFolder, "aa", "aa11.jpg");

    private string PooledPreview() =>
        Path.Combine(_house.SharedFolder, SharedFolderPool.ThumbsFolder, "aa", "aa11-p.jpg");

    /// <summary>
    /// A set from <see cref="Elsewhere"/> with every list empty, with
    /// <paramref name="members"/> written over the top of it. One shape serves
    /// all three kinds of file, because each skips what it has never heard of.
    /// </summary>
    private static string Whole(string members)
    {
        var json = JsonNode.Parse(
            $$"""
            {
              "Machine": {"Id":"{{Elsewhere.Id:D}}","Name":"{{Elsewhere.Name}}","AppVersion":"1.0.0","SchemaVersion":1},
              "WrittenUtc": "2026-03-02T09:00:00Z",
              "Sources": [], "People": [], "Answers": [], "Strangers": [], "Turns": [],
              "Albums": [], "Memberships": [], "Rejections": [], "Eras": [], "Links": [],
              "Models": {}, "Faces": [], "Facts": []
            }
            """)!.AsObject();

        foreach ((string name, JsonNode? value) in
                 JsonNode.Parse(members)!.AsObject().ToList())
        {
            json[name] = value?.DeepClone();
        }

        return json.ToJsonString();
    }

    private static MemoryStream Gzipped(string json)
    {
        var stream = new MemoryStream();

        using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(json));
        }

        stream.Position = 0;
        return stream;
    }

    private async Task PlantAsync(string folder, string extension, string json)
    {
        string directory = Path.Combine(_house.SharedFolder, folder);
        Directory.CreateDirectory(directory);

        await using var file = File.Create(Path.Combine(directory, $"{Guid.NewGuid():D}{extension}"));
        await Gzipped(json).CopyToAsync(file);
    }

    public void Dispose() => _house.Dispose();
}

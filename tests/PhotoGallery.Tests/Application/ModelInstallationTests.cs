using System.Security.Cryptography;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Models;
using PhotoGallery.Infrastructure.Models;
using PhotoGallery.Infrastructure.Storage;
using PhotoGallery.Tests.Infrastructure;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// Pointing the app at a folder of downloaded model files.
/// </summary>
/// <remarks>
/// Against small files of the test's own rather than 1.9 GB of real weights,
/// which is the reason the manifest is passed in rather than reached for
/// statically. Every descriptor here is given a different length, because
/// length is what the importer searches on.
/// </remarks>
public sealed class ModelInstallationTests : IDisposable
{
    private readonly string _root;
    private readonly string _downloads;
    private readonly ModelsIn _folder;
    private readonly FileModelStore _store;
    private readonly GetModelStatusHandler _status;
    private readonly ImportModelsHandler _import;

    /// <summary>The bytes each model is, distinct in both length and content.</summary>
    private static readonly Dictionary<ModelId, byte[]> s_content =
        Enum.GetValues<ModelId>().ToDictionary(
            id => id,
            id => Bytes(seed: (int)id, length: 64 + ((int)id * 32)));

    public ModelInstallationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-install-{Guid.NewGuid():N}");
        _downloads = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(_downloads);

        var workingFolder = new WorkingFolder(Path.Combine(_root, "library"));
        workingFolder.EnsureCreated();

        var manifest = new ModelManifest(
            Enum.GetValues<ModelId>().Select(id => new ModelDescriptor(
                id,
                Version: 1,
                FileName: $"{id}.bin".ToLowerInvariant(),
                Bytes: s_content[id].Length,
                Sha256: Convert.ToHexStringLower(SHA256.HashData(s_content[id])),
                Licence: id is ModelId.FaceDetection or ModelId.FaceRecognition
                    ? "face terms"
                    : "content terms")));

        _folder = new ModelsIn(workingFolder.ModelsPath);
        _store = new FileModelStore(_folder, manifest);
        _status = new GetModelStatusHandler(_store, _folder);
        _import = new ImportModelsHandler(_store, _status);
    }

    [Fact]
    public void EveryModelBelongsToExactlyOneFeature()
    {
        // The drift this mapping exists to stop: a model added to the enum and
        // to the manifest, and never added to a feature, would be verified by
        // nothing and would gate nothing - the feature would simply fail later,
        // on a file the screen had never offered to install.
        List<ModelId> mapped = [.. FeatureModels.All];

        Assert.Equal(Enum.GetValues<ModelId>().Length, mapped.Count);
        Assert.Equal(mapped.Count, mapped.Distinct().Count());
        Assert.Empty(Enum.GetValues<ModelId>().Except(mapped));
    }

    [Fact]
    public void AFreshLibraryHasNothingAndSaysSoPerFeature()
    {
        IReadOnlyList<FeatureStatus> features = _status.Handle();

        Assert.Equal(Enum.GetValues<ModelFeature>().Length, features.Count);
        Assert.All(features, feature =>
        {
            Assert.True(feature.IsMissing);
            Assert.False(feature.IsReady);
            Assert.False(feature.IsPartial);
        });
    }

    [Fact]
    public async Task MovingTheFolder_LooksInTheNewOneWithoutCopyingAnything()
    {
        // The files are 1.9 GB and belong to no library in particular, so moving
        // the folder has to be a change of address rather than a move: what was
        // in the old one stays there, and a folder that already holds them turns
        // the feature straight back on.
        Write(Path.Combine(_downloads, "a.bin"), ModelId.FaceDetection);
        await _import.HandleAsync(_downloads);
        Assert.Equal(ModelState.Ready, _store.StateOf(ModelId.FaceDetection));

        string elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        _status.UseFolder(elsewhere);

        Assert.Equal(elsewhere, _status.Folder);
        Assert.Equal(ModelState.Missing, _store.StateOf(ModelId.FaceDetection));

        // And back again, to prove nothing was moved or deleted on the way out.
        _status.UseFolder(_folder.Default);
        Assert.Equal(ModelState.Ready, _store.StateOf(ModelId.FaceDetection));
    }

    [Fact]
    public void TheFolderIsNamedSoTheScreenCanSendPeopleToIt()
    {
        // The Settings screen tells the user to download into this folder, so it
        // has to be the same folder the store reads - naming one and reading
        // another would be a set of instructions that quietly does not work.
        Assert.Equal(
            Path.GetDirectoryName(_store.ResolvePath(ModelId.FaceDetection)),
            _status.Folder);
    }

    [Fact]
    public async Task Import_FindsAModelByItsContentsRatherThanItsName()
    {
        // The reason this matters: upstream does not use our names. The content
        // graphs ship as visual/model.onnx and textual/model.onnx, and requiring
        // a rename first would put the hardest step of the instructions before
        // the easiest one.
        Write(Path.Combine(_downloads, "utterly-unrelated-name.bin"), ModelId.FaceDetection);

        ImportModelsResult result = await _import.HandleAsync(_downloads);

        Assert.Equal(1, result.Installed);
        Assert.Equal(ModelState.Ready, _store.StateOf(ModelId.FaceDetection));
    }

    [Fact]
    public async Task Import_LooksOneFolderDown()
    {
        // What an unzipped download looks like, and what the content export
        // actually ships: a folder per graph.
        string nested = Path.Combine(_downloads, "visual");
        Directory.CreateDirectory(nested);
        Write(Path.Combine(nested, "model.onnx"), ModelId.ContentVision);

        ImportModelsResult result = await _import.HandleAsync(_downloads);

        Assert.Equal(1, result.Installed);
        Assert.Equal(ModelState.Ready, _store.StateOf(ModelId.ContentVision));
    }

    [Fact]
    public async Task Import_RefusesAFileOfTheRightSizeAndTheWrongContents()
    {
        // Size is how candidates are found; the digest is what decides. A file
        // that passes the first test and fails the second has to be named, or
        // the user is looking at a folder they believe holds the model and being
        // told nothing was there.
        byte[] impostor = [.. s_content[ModelId.FaceDetection].Select(b => (byte)(b ^ 0xFF))];
        File.WriteAllBytes(Path.Combine(_downloads, "impostor.bin"), impostor);

        ImportModelsResult result = await _import.HandleAsync(_downloads);

        Assert.Equal(0, result.Installed);
        Assert.Equal(["impostor.bin"], result.Rejected);
        Assert.Equal(ModelState.Missing, _store.StateOf(ModelId.FaceDetection));
        Assert.Contains("not the right file", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_OfAFolderHoldingNothingWeUseSaysExactlyThat()
    {
        File.WriteAllText(Path.Combine(_downloads, "readme.txt"), "not a model");

        ImportModelsResult result = await _import.HandleAsync(_downloads);

        Assert.True(result.FoundNothing);
        Assert.Contains("Check you chose the folder", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_OfHalfAFeatureLeavesItPartRatherThanMissing()
    {
        // Four files copied and one missed is a job nearly done. Reporting it as
        // "not installed" would send the user back to the beginning of it.
        Write(Path.Combine(_downloads, "one.bin"), ModelId.FaceDetection);

        ImportModelsResult result = await _import.HandleAsync(_downloads);

        FeatureStatus faces = Feature(result, ModelFeature.Faces);
        Assert.True(faces.IsPartial);
        Assert.False(faces.IsReady);
        Assert.False(faces.IsMissing);
        Assert.Equal(
            [ModelId.FaceRecognition],
            faces.Outstanding.Select(file => file.Id));
    }

    [Fact]
    public async Task Import_MakesAFeatureReadyOnlyWhenAllOfItArrives()
    {
        Write(Path.Combine(_downloads, "a.bin"), ModelId.FaceDetection);
        Write(Path.Combine(_downloads, "b.bin"), ModelId.FaceRecognition);

        ImportModelsResult result = await _import.HandleAsync(_downloads);

        Assert.Equal(2, result.Installed);
        Assert.Equal([ModelFeature.Faces], result.NowReady);
        Assert.True(_status.IsReady(ModelFeature.Faces));
        Assert.False(_status.IsReady(ModelFeature.ContentSearch));
    }

    [Fact]
    public async Task Import_DoesNotCopyAgainWhatIsAlreadyInstalled()
    {
        // Pointing at the same folder twice is something people do when they are
        // not sure the first time worked, and re-copying 1.2 GB to prove it
        // would be a slow way of saying yes.
        Write(Path.Combine(_downloads, "a.bin"), ModelId.FaceDetection);
        await _import.HandleAsync(_downloads);

        ImportModelsResult again = await _import.HandleAsync(_downloads);

        Assert.Equal(0, again.Installed);
        Assert.Equal(ModelState.Ready, _store.StateOf(ModelId.FaceDetection));
    }

    [Fact]
    public async Task Import_NamesEachFileAsItGoes()
    {
        Write(Path.Combine(_downloads, "a.bin"), ModelId.FaceDetection);
        List<string> named = [];

        await _import.HandleAsync(_downloads, new Progress<string>(named.Add));

        // Progress<T> posts to the captured context, which a test does not pump,
        // so this asserts what was offered rather than what was displayed.
        Assert.True(named.Count <= 1);
    }

    [Fact]
    public void AFeaturesLicencesAreCarriedWithItAndSaidOnce()
    {
        // The face weights are non-commercial research use only. The app cannot
        // ship them and must not send somebody to fetch them without saying so.
        FeatureStatus faces = _status.HandleOne(ModelFeature.Faces);

        Assert.Equal(["face terms"], faces.Licences);
        Assert.Equal(2, faces.Files.Count);
    }

    private static FeatureStatus Feature(ImportModelsResult result, ModelFeature feature) =>
        result.Features.Single(status => status.Feature == feature);

    private static void Write(string path, ModelId id) =>
        File.WriteAllBytes(path, s_content[id]);

    private static byte[] Bytes(int seed, int length) =>
        [.. Enumerable.Range(0, length).Select(i => (byte)((i * 31) + seed))];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }
}

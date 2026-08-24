using System.Security.Cryptography;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Models;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Exercised against a small file of the test's own rather than the real 182 MB
/// of weights, which is the reason the manifest is passed in rather than reached
/// for statically.
/// </summary>
public sealed class FileModelStoreTests : IDisposable
{
    private const string FileName = "test-model.onnx";

    private static readonly byte[] s_content = [.. Enumerable.Range(0, 4096).Select(i => (byte)i)];

    private readonly string _root;
    private readonly string _sourceFolder;
    private readonly WorkingFolder _workingFolder;
    private readonly FileModelStore _store;

    public FileModelStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-models-{Guid.NewGuid():N}");
        _sourceFolder = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(_sourceFolder);

        _workingFolder = new WorkingFolder(Path.Combine(_root, "library"));
        _workingFolder.EnsureCreated();

        var manifest = new ModelManifest(
        [
            new ModelDescriptor(
                ModelId.FaceDetection,
                Version: 1,
                FileName,
                s_content.Length,
                Convert.ToHexStringLower(SHA256.HashData(s_content)),
                Licence: "test"),
        ]);

        _store = new FileModelStore(new ModelsIn(_workingFolder.ModelsPath), manifest);
    }

    [Fact]
    public void StateOf_IsMissingWhenNothingHasBeenImported()
    {
        Assert.Equal(ModelState.Missing, _store.StateOf(ModelId.FaceDetection));
    }

    [Fact]
    public async Task Import_OfTheRightFileMakesItReady()
    {
        ModelState state = await _store.ImportAsync(ModelId.FaceDetection, WriteSource(s_content));

        Assert.Equal(ModelState.Ready, state);
        Assert.Equal(ModelState.Ready, _store.StateOf(ModelId.FaceDetection));
        Assert.True(File.Exists(_store.ResolvePath(ModelId.FaceDetection)));
    }

    [Fact]
    public async Task Import_OfATruncatedFileLeavesNothingTheAppWouldUse()
    {
        ModelState state = await _store.ImportAsync(
            ModelId.FaceDetection, WriteSource(s_content[..1024]));

        Assert.Equal(ModelState.Damaged, state);
        Assert.False(File.Exists(_store.ResolvePath(ModelId.FaceDetection)));
        Assert.Empty(PartialFiles());
    }

    [Fact]
    public async Task Import_OfTheRightLengthButTheWrongBytesIsStillRefused()
    {
        // The length check is only an early reject. Without the digest this file
        // would pass, and ONNX Runtime would fail later in a way that reads as
        // the model being wrong rather than the file being someone else's.
        byte[] impostor = [.. s_content];
        impostor[^1] ^= 0xFF;

        ModelState state = await _store.ImportAsync(ModelId.FaceDetection, WriteSource(impostor));

        Assert.Equal(ModelState.Damaged, state);
        Assert.False(File.Exists(_store.ResolvePath(ModelId.FaceDetection)));
    }

    [Fact]
    public async Task StateOf_RemovesAFileThatWasCorruptedAfterItWasImported()
    {
        await _store.ImportAsync(ModelId.FaceDetection, WriteSource(s_content));
        await File.WriteAllBytesAsync(_store.ResolvePath(ModelId.FaceDetection), s_content[..512]);

        Assert.Equal(ModelState.Damaged, _store.StateOf(ModelId.FaceDetection));

        // Deleted, so the next start reports Missing and offers the import again
        // rather than re-reading a file already known to be broken.
        Assert.False(File.Exists(_store.ResolvePath(ModelId.FaceDetection)));
        Assert.Equal(ModelState.Missing, _store.StateOf(ModelId.FaceDetection));
    }

    [Fact]
    public async Task Import_RepairsAModelThatIsAlreadyThereButWrong()
    {
        await File.WriteAllBytesAsync(_store.ResolvePath(ModelId.FaceDetection), s_content[..64]);

        ModelState state = await _store.ImportAsync(ModelId.FaceDetection, WriteSource(s_content));

        Assert.Equal(ModelState.Ready, state);
        Assert.Equal(s_content.Length, new FileInfo(_store.ResolvePath(ModelId.FaceDetection)).Length);
    }

    [Fact]
    public async Task Import_FromAFileThatIsNotThereChangesNothing()
    {
        ModelState state = await _store.ImportAsync(
            ModelId.FaceDetection, Path.Combine(_sourceFolder, "absent.onnx"));

        Assert.Equal(ModelState.Missing, state);
        Assert.Empty(PartialFiles());
    }

    [Fact]
    public void Describe_ReportsWhatTheManifestRequires()
    {
        ModelDescriptor descriptor = _store.Describe(ModelId.FaceDetection);

        Assert.Equal(FileName, descriptor.FileName);
        Assert.Equal(s_content.Length, descriptor.Bytes);
    }

    [Fact]
    public void Describe_RefusesAModelTheManifestDoesNotName()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _store.Describe(ModelId.FaceRecognition));
    }

    /// <summary>
    /// Every model the app knows about, and nothing it does not.
    /// </summary>
    /// <remarks>
    /// A count rather than a loose "at least": the manifest is what decides
    /// whether a file on disk is trusted, and one appearing in it that nobody
    /// meant to add is exactly the change that should have to be deliberate.
    /// </remarks>
    [Fact]
    public void DefaultManifest_NamesTheFaceModelsAndTheContentSearchFilesAndNothingElse()
    {
        // Six, and the gazetteer is deliberately not among them: it is compiled
        // into the executable rather than supplied by the user, so there is no
        // file to verify.
        Assert.Equal(6, ModelManifest.Default.All.Count);

        Assert.Equal("det_10g.onnx", ModelManifest.Default.For(ModelId.FaceDetection).FileName);
        Assert.Equal("w600k_r50.onnx", ModelManifest.Default.For(ModelId.FaceRecognition).FileName);
        Assert.Equal(
            "clip_vit_l14_visual.onnx", ModelManifest.Default.For(ModelId.ContentVision).FileName);
        Assert.Equal(
            "clip_vit_l14_textual.onnx", ModelManifest.Default.For(ModelId.ContentText).FileName);
        Assert.Equal(
            "clip_vit_l14_vocab.json",
            ModelManifest.Default.For(ModelId.ContentVocabulary).FileName);
        Assert.Equal(
            "clip_vit_l14_merges.txt", ModelManifest.Default.For(ModelId.ContentMerges).FileName);
    }

    /// <summary>
    /// Every descriptor carries a real digest and a real size.
    /// </summary>
    /// <remarks>
    /// StateOf deletes a file whose digest does not match, so a descriptor
    /// carrying a placeholder is not a missing check - it is an instruction to
    /// destroy something the user downloaded. Length 64 catches an abbreviated
    /// paste, lower case catches one taken from a tool that shouts, and a
    /// non-zero size catches the entry nobody finished.
    /// </remarks>
    [Fact]
    public void DefaultManifest_DescribesEveryFileWellEnoughToVerifyIt()
    {
        Assert.All(ModelManifest.Default.All, descriptor =>
        {
            Assert.Equal(64, descriptor.Sha256.Length);
            Assert.Equal(descriptor.Sha256.ToLowerInvariant(), descriptor.Sha256);
            Assert.True(descriptor.Sha256.All(Uri.IsHexDigit), descriptor.FileName);
            Assert.True(descriptor.Bytes > 0, descriptor.FileName);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Licence), descriptor.FileName);
        });
    }

    private string WriteSource(byte[] bytes)
    {
        string path = Path.Combine(_sourceFolder, $"source-{Guid.NewGuid():N}.onnx");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string[] PartialFiles() =>
        Directory.GetFiles(_workingFolder.ModelsPath, "*.partial");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not go is not a failed test.
        }
    }
}

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Models;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Models;
using PhotoGallery.Infrastructure.Models;
using PhotoGallery.Infrastructure.Storage;
using PhotoGallery.Tests.Infrastructure;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Changing where the model files are kept, while the screen is reading them.
/// </summary>
/// <remarks>
/// The bug this exists for. Closing the folder picker brings the window back to
/// the front, the front sets off a re-read of the folder, and the choice the
/// user had just made arrived a moment later to find the screen busy - so it was
/// dropped, silently, and the folder never changed however many times they
/// tried.
///
/// <para>Reads may skip each other, because two reads of one folder have the
/// same answer. A choice is the one thing here a person asked for by hand, and
/// it waits its turn instead.</para>
/// </remarks>
public sealed class ModelFolderChoiceTests : IDisposable
{
    [Fact]
    public async Task AChoiceMadeWhileTheFolderIsBeingReadIsNotDropped()
    {
        // Not awaited: RefreshAsync marks the screen busy before its first
        // await, which is exactly the state the picker used to return into.
        Task reading = _models.RefreshAsync();

        await _models.ChooseFolderAsync(_elsewhere);
        await reading;

        Assert.Equal(_elsewhere, _models.Folder);
        Assert.Equal(_elsewhere, _folder.Path);
    }

    [Fact]
    public async Task ChoosingAFolderWithTheFilesInItTurnsTheFeatureOn()
    {
        Write(Path.Combine(_elsewhere, "det.bin"), ModelId.FaceDetection);
        Write(Path.Combine(_elsewhere, "rec.bin"), ModelId.FaceRecognition);

        await _models.ChooseFolderAsync(_elsewhere);

        Assert.True(_models.IsReady(ModelFeature.Faces));
        Assert.Contains(_elsewhere, _models.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChoosingAnEmptyFolderSaysSoRatherThanLookingUnchanged()
    {
        // Both cards read "not installed" before and after, so without a word
        // from the app the change is indistinguishable from being ignored -
        // which is what it looked like when it really was being ignored.
        await _models.ChooseFolderAsync(_elsewhere);

        Assert.False(_models.IsReady(ModelFeature.Faces));
        Assert.Contains("Nothing this app recognises", _models.Status, StringComparison.Ordinal);
    }

    private readonly string _root;
    private readonly string _elsewhere;
    private readonly ModelsIn _folder;
    private readonly ServiceProvider _services;
    private readonly ModelsViewModel _models;

    /// <summary>The bytes each model is, distinct in both length and content.</summary>
    private static readonly Dictionary<ModelId, byte[]> s_content =
        Enum.GetValues<ModelId>().ToDictionary(
            id => id,
            id => Bytes(seed: (int)id, length: 64 + ((int)id * 32)));

    public ModelFolderChoiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-choose-{Guid.NewGuid():N}");
        _elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(_elsewhere);

        var workingFolder = new WorkingFolder(Path.Combine(_root, "library"));
        workingFolder.EnsureCreated();

        var manifest = new ModelManifest(
            Enum.GetValues<ModelId>().Select(id => new ModelDescriptor(
                id,
                Version: 1,
                FileName: $"{id}.bin".ToLowerInvariant(),
                Bytes: s_content[id].Length,
                Sha256: Convert.ToHexStringLower(SHA256.HashData(s_content[id])),
                Licence: "test terms")));

        // A folder double, so the choice is not written into the config file
        // beside the test runner.
        _folder = new ModelsIn(workingFolder.ModelsPath);
        var store = new FileModelStore(_folder, manifest);

        _services = new ServiceCollection()
            .AddSingleton<IModelFolder>(_folder)
            .AddSingleton<IModelStore>(store)
            .AddScoped<GetModelStatusHandler>()
            .AddScoped<ImportModelsHandler>()
            .BuildServiceProvider();

        _models = new ModelsViewModel(_services.GetRequiredService<IServiceScopeFactory>());
    }

    private void Write(string path, ModelId id) =>
        File.WriteAllBytes(path, s_content[id]);

    private static byte[] Bytes(int seed, int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)((seed * 31) + i);
        }

        return bytes;
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is not a failed test.
        }
    }
}

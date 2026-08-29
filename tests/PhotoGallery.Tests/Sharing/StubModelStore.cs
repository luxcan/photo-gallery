using PhotoGallery.Application.Ports;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Stands in for the model files, which these tests have none of.
/// </summary>
/// <remarks>
/// It answers one question: which models this library is running, and what their
/// files hash to. That is the whole of what the vector check looks at - an
/// embedding is meaningless outside the model that produced it - so a stub that
/// always agreed would leave the only interesting case untested.
/// </remarks>
internal sealed class StubModelStore : IModelStore
{
    private readonly Dictionary<ModelId, string> _digests = new()
    {
        [ModelId.FaceDetection] = "detect-v1",
        [ModelId.FaceRecognition] = "recognise-v1",
    };

    /// <summary>Says this library is running a different build of one model.</summary>
    public StubModelStore Running(ModelId id, string digest)
    {
        _digests[id] = digest;
        return this;
    }

    /// <summary>Says this library has not installed a model at all.</summary>
    public StubModelStore Without(ModelId id)
    {
        _digests.Remove(id);
        return this;
    }

    public ModelDescriptor Describe(ModelId id) =>
        new(id, 1, $"{id}.onnx", 1024, _digests.GetValueOrDefault(id, string.Empty), "test");

    public string ResolvePath(ModelId id) => Path.Combine(Path.GetTempPath(), $"{id}.onnx");

    public ModelState StateOf(ModelId id) =>
        _digests.ContainsKey(id) ? ModelState.Ready : ModelState.Missing;

    public Task<ModelState> ImportAsync(
        ModelId id, string sourcePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(StateOf(id));
}

using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Models;

namespace PhotoGallery.Measure;

/// <summary>
/// The app's model manifest over a folder this harness must not change.
/// </summary>
/// <remarks>
/// <see cref="FileModelStore"/> deletes a file that fails its digest, which is
/// right for the app's own models folder and wrong for somebody else's - the
/// folder given here may be InsightFace's own download. This one only answers.
/// </remarks>
internal sealed class ReadOnlyModelStore : IModelStore
{
    private readonly string _folder;

    public ReadOnlyModelStore(string folder) => _folder = folder;

    public ModelDescriptor Describe(ModelId id) => ModelManifest.Default.For(id);

    public string ResolvePath(ModelId id) => Path.Combine(_folder, Describe(id).FileName);

    public ModelState StateOf(ModelId id)
    {
        string path = ResolvePath(id);
        if (!File.Exists(path))
        {
            return ModelState.Missing;
        }

        ModelDescriptor descriptor = Describe(id);
        return ModelFile.Matches(path, descriptor.Bytes, descriptor.Sha256)
            ? ModelState.Ready
            : ModelState.Damaged;
    }

    public Task<ModelState> ImportAsync(
        ModelId id, string sourcePath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The measurement harness never installs a model.");
}

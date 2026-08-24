namespace PhotoGallery.Application.Ports;

/// <summary>
/// The model files under the working folder, and whether they can be trusted.
/// </summary>
/// <remarks>
/// A feature learns where its weights are only through here, because a model is
/// usable only once its size and digest match the manifest. Two features will
/// eventually want weights and they arrive differently - one bundled, one
/// fetched - so what proves a file whole lives in one place rather than twice.
/// </remarks>
public interface IModelStore
{
    /// <summary>What the manifest says this model has to be.</summary>
    ModelDescriptor Describe(ModelId id);

    /// <summary>
    /// Where the file belongs, whether or not anything is there.
    /// </summary>
    /// <remarks>
    /// For naming a location to the user. Ask <see cref="StateOf"/> before
    /// opening it: a path is not a promise that the file is the right one.
    /// </remarks>
    string ResolvePath(ModelId id);

    /// <summary>
    /// Whether the file on disk is the one the manifest describes, removing it
    /// when it is not.
    /// </summary>
    /// <remarks>
    /// Reads and digests the whole file, so this costs around a second for the
    /// 166 MB recognition model. Asked once when a feature starts, never per
    /// item. A file that fails is deleted, because leaving it would make every
    /// later start re-verify, and fail on, something already known to be broken.
    /// </remarks>
    ModelState StateOf(ModelId id);

    /// <summary>
    /// Copies a file the user already has into the working folder, keeping it
    /// only if it matches the manifest.
    /// </summary>
    /// <remarks>
    /// This is what serves a machine with no internet, a network that blocks the
    /// download, or someone who already has the weights. The copy is written to
    /// <c>&lt;name&gt;.partial</c> and renamed only once the digest matches, so
    /// an interrupted copy can never leave a file the app would go on to use.
    /// </remarks>
    Task<ModelState> ImportAsync(
        ModelId id,
        string sourcePath,
        CancellationToken cancellationToken = default);
}

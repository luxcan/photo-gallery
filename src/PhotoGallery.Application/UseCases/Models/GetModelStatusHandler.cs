using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Models;

/// <summary>
/// What each optional feature needs on disk, and how much of it is there.
/// </summary>
/// <remarks>
/// The app ships without weights: they are 1.9 GB, and the face pack is licensed
/// for non-commercial use only, so neither can travel inside the executable.
/// Everything that reads a model already refuses politely when one is missing -
/// this is what lets a screen say so <em>before</em> the user presses anything.
///
/// <para>The first call is expensive. Proving a model means digesting it, and
/// the vision graph alone is 1.2 GB, so this belongs off the dispatcher. Every
/// call after it is free: the store remembers its answer against the file's
/// length and last-write time, and forgets it the moment either changes.</para>
/// </remarks>
public sealed class GetModelStatusHandler
{
    private readonly IModelStore _models;
    private readonly IModelFolder _folder;

    public GetModelStatusHandler(IModelStore models, IModelFolder folder)
    {
        _models = models;
        _folder = folder;
    }

    /// <summary>
    /// Where the files belong, whether or not any of them are there yet.
    /// </summary>
    /// <remarks>
    /// Named so the screen can tell somebody where to put a download rather than
    /// asking them to find it again afterwards. It is not created here: opening
    /// it is what makes it, and until then there is nothing to look at.
    /// </remarks>
    public string Folder => _folder.Path;

    /// <summary>Points the app at another folder and reports what is in it.</summary>
    public IReadOnlyList<FeatureStatus> UseFolder(string folder)
    {
        _folder.Use(folder);
        return Handle();
    }

    /// <summary>Every feature, in the order the Settings screen lists them.</summary>
    public IReadOnlyList<FeatureStatus> Handle() =>
        [.. Enum.GetValues<ModelFeature>().Select(HandleOne)];

    public FeatureStatus HandleOne(ModelFeature feature) =>
        new(feature, [.. FeatureModels.Of(feature).Select(Describe)]);

    /// <summary>Whether a feature can run right now, without describing why.</summary>
    public bool IsReady(ModelFeature feature) =>
        FeatureModels.Of(feature).All(id => _models.StateOf(id) == ModelState.Ready);

    private ModelFileStatus Describe(ModelId id)
    {
        ModelDescriptor descriptor = _models.Describe(id);

        return new ModelFileStatus(
            id,
            descriptor.FileName,
            descriptor.Bytes,
            descriptor.Licence,
            _models.StateOf(id));
    }
}

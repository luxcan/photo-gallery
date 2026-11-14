using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>Whether this computer can read eyes, for the keeper ladder.</summary>
public static class EyeReading
{
    /// <summary>
    /// True when the landmark model is installed and so is the face detector it
    /// leans on for which way a face is turned.
    /// </summary>
    /// <remarks>
    /// Asked by both the pass that measures and the screen that shows the
    /// answer, so a computer whose eye model has gone stops naming closed eyes
    /// at once rather than trusting what was read before it went.
    /// </remarks>
    public static bool IsAvailable(IModelStore models)
    {
        ArgumentNullException.ThrowIfNull(models);

        return FeatureModels.Of(ModelFeature.Eyes).All(id => models.StateOf(id) == ModelState.Ready)
               && models.StateOf(ModelId.FaceDetection) == ModelState.Ready;
    }
}

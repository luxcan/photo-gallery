namespace PhotoGallery.Application.Ports;

/// <summary>
/// Which model files each feature cannot start without.
/// </summary>
/// <remarks>
/// One place, because three callers need the same answer and they disagreed
/// quietly when they each held their own copy: the face pass listed two files,
/// the content pass listed four, and the screen that offers to install them
/// listed none at all. A model added to a feature has to be added here, and
/// everything that gates on that feature follows.
/// </remarks>
public static class FeatureModels
{
    private static readonly ModelId[] s_faces =
        [ModelId.FaceDetection, ModelId.FaceRecognition];

    private static readonly ModelId[] s_contentSearch =
    [
        ModelId.ContentVision,
        ModelId.ContentText,
        ModelId.ContentVocabulary,
        ModelId.ContentMerges,
    ];

    /// <summary>The files that feature needs, in the order they are worth naming.</summary>
    public static IReadOnlyList<ModelId> Of(ModelFeature feature) => feature switch
    {
        ModelFeature.Faces => s_faces,
        ModelFeature.ContentSearch => s_contentSearch,
        _ => throw new ArgumentOutOfRangeException(nameof(feature)),
    };

    /// <summary>Every model the app knows how to use, feature by feature.</summary>
    public static IEnumerable<ModelId> All =>
        Enum.GetValues<ModelFeature>().SelectMany(Of);
}

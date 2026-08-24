namespace PhotoGallery.Application.Ports;

/// <summary>
/// Something the app can only do once a set of model files is on disk.
/// </summary>
/// <remarks>
/// The unit the user installs and the unit the app enables are the same thing,
/// and neither is a single file: finding people needs a detector and a
/// recogniser, and searching by content needs two graphs and the tokenizer the
/// pair was trained on. Half of either is no feature at all.
/// </remarks>
public enum ModelFeature
{
    /// <summary>Finding faces, and naming the people they belong to.</summary>
    Faces = 0,

    /// <summary>Describing photographs, so they can be searched by what is in them.</summary>
    ContentSearch = 1,
}

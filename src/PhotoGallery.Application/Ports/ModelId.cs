namespace PhotoGallery.Application.Ports;

/// <summary>A machine-learning model this app needs on disk before a feature works.</summary>
public enum ModelId
{
    /// <summary>Finds where the faces are in a picture.</summary>
    FaceDetection = 0,

    /// <summary>Turns one aligned face into the vector that identifies it.</summary>
    FaceRecognition = 1,

    /// <summary>Turns a photograph into the vector that says what it is of.</summary>
    ContentVision = 2,

    /// <summary>
    /// Turns a typed phrase into a vector in that same space, which is what makes
    /// searching by description possible at all.
    /// </summary>
    ContentText = 3,

    /// <summary>
    /// The words the text encoder knows, and the number it holds each under.
    /// </summary>
    /// <remarks>
    /// Not a graph, but it belongs here for the same reason the graphs do: it has
    /// to be present and it has to be the right file. A vocabulary that differs
    /// from the one the encoder was trained on does not fail - it produces a
    /// confident vector for the wrong words.
    /// </remarks>
    ContentVocabulary = 4,

    /// <summary>
    /// The order in which pairs of characters are joined into longer pieces.
    /// </summary>
    /// <remarks>
    /// Useless without the vocabulary and vice versa: the two were written by one
    /// training run and describe one scheme between them.
    /// </remarks>
    ContentMerges = 5,
}

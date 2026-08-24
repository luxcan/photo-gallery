using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Models;

/// <summary>
/// Where each model file can be fetched from, one address per file.
/// </summary>
/// <remarks>
/// Per file rather than one page per feature, so the screen can list what it
/// needs and make each line the thing that fetches it. A page leaves the reader
/// to work out which of its files are wanted; four links leave nothing to work
/// out.
///
/// <para>Every address is pinned to a fixed revision. Both repositories have
/// been re-uploaded in place - the content export more than once - and an
/// earlier revision of the same file is a few hundred kilobytes different, so it
/// fails the digest with nothing on screen to explain why. "Latest" is the one
/// thing these must never say.</para>
///
/// <para>The face files are taken from the mirror rather than the release zip
/// because the zip is 289 MB to extract 191 MB of wanted files, and its two are
/// already under the names this app uses. Both were checked against the digests
/// in the manifest.</para>
/// </remarks>
public static class ModelSources
{
    private const string Faces =
        "https://huggingface.co/public-data/insightface/resolve/"
        + "53577c23cb5e1c5fd0f632d6a1353e8e87b44986/models/buffalo_l/";

    private const string Content =
        "https://huggingface.co/immich-app/ViT-L-14__openai/resolve/"
        + "9b27c6b49d2ff4957fa0b5a25521eee12f109543/";

    /// <summary>The address that yields exactly the file the manifest describes.</summary>
    public static string? Of(ModelId id) => id switch
    {
        ModelId.FaceDetection => $"{Faces}det_10g.onnx",
        ModelId.FaceRecognition => $"{Faces}w600k_r50.onnx",

        // Both graphs are called model.onnx where they come from, which is why
        // the screen lists the name this app wants beside each link.
        ModelId.ContentVision => $"{Content}visual/model.onnx",
        ModelId.ContentText => $"{Content}textual/model.onnx",
        ModelId.ContentVocabulary => $"{Content}textual/vocab.json",
        ModelId.ContentMerges => $"{Content}textual/merges.txt",
        _ => null,
    };
}

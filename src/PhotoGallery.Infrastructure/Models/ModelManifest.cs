using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Models;

/// <summary>
/// The models this app knows about, and what each file has to be.
/// </summary>
/// <remarks>
/// Passed to the store rather than reached for statically, so a test can describe
/// a small file of its own instead of needing 182 MB of real weights on disk to
/// exercise verification at all.
/// </remarks>
public sealed class ModelManifest
{
    /// <summary>
    /// InsightFace's library is MIT, but its pretrained weights are research-only
    /// and ship with no licence file at all. Fine for personal use; a blocker if
    /// this is ever sold, which is why the models sit behind a port.
    /// </summary>
    private const string InsightFaceLicence =
        "InsightFace buffalo_l weights - non-commercial research use only.";

    /// <summary>
    /// OpenAI's CLIP weights are MIT. The ONNX export used here is the Immich
    /// project's, whose repository declares no licence of its own - so the
    /// permission comes from upstream and the packaging is unstated, which is
    /// worth recording rather than rounding to "MIT" and hoping.
    /// </summary>
    private const string ClipLicence =
        "OpenAI CLIP ViT-L/14. OpenAI published CLIP under the MIT licence and "
        + "describes the weights as intended for research; the ONNX export is the "
        + "Immich project's and states no licence of its own.";

    // The gazetteer is deliberately not here. This manifest decides whether a
    // file the *user* supplied can be trusted; the places data is compiled into
    // the executable, so there is nothing to verify and nothing to install.
    // See PhotoGallery.Infrastructure.Places.GeoNamesGazetteer.

    private readonly Dictionary<ModelId, ModelDescriptor> _byId;

    public ModelManifest(IEnumerable<ModelDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _byId = descriptors.ToDictionary(descriptor => descriptor.Id);
    }

    /// <summary>
    /// The two face models, measured from the buffalo_l release actually in use
    /// on 15 August 2026.
    /// </summary>
    /// <remarks>
    /// Only detection and recognition. The pack also carries landmark and
    /// gender/age graphs this app never asks for, and loading them measured 3.4x
    /// slower for an identical result.
    ///
    /// <para>Digests are taken from the files themselves. One invented rather
    /// than measured would be a lie the verifier believes.</para>
    /// </remarks>
    public static ModelManifest Default { get; } = new(
    [
        new ModelDescriptor(
            ModelId.FaceDetection,
            Version: 1,
            FileName: "det_10g.onnx",
            Bytes: 16_923_827,
            Sha256: "5838f7fe053675b1c7a08b633df49e7af5495cee0493c7dcf6697200b85b5b91",
            Licence: InsightFaceLicence),
        new ModelDescriptor(
            ModelId.FaceRecognition,
            Version: 1,
            FileName: "w600k_r50.onnx",
            Bytes: 174_383_860,
            Sha256: "4c06341c33c2ca1f86781dab0e829f88ad5b64be9fba56e56bc9ebdefc619e43",
            Licence: InsightFaceLicence),

        // Content search, measured from the files on 16 August 2026. The two
        // graphs are a trained pair and the tokenizer is the vocabulary that
        // pair was trained on: a mismatched one of any of the three does not
        // fail, it answers confidently about the wrong thing.
        new ModelDescriptor(
            ModelId.ContentVision,
            Version: 1,
            FileName: "clip_vit_l14_visual.onnx",
            Bytes: 1_216_297_719,
            Sha256: "2b02d572f59c509f4b97b9c54a868453cca1a652cd5d60e1d51d0052f055cb8c",
            Licence: ClipLicence),
        new ModelDescriptor(
            ModelId.ContentText,
            Version: 1,
            FileName: "clip_vit_l14_textual.onnx",
            Bytes: 495_082_255,
            Sha256: "9fbe72ea8d36c2effaccedcf7249e3729ad0d9b4af6604b433ecdd0105663c9c",
            Licence: ClipLicence),
        new ModelDescriptor(
            ModelId.ContentVocabulary,
            Version: 1,
            FileName: "clip_vit_l14_vocab.json",
            Bytes: 862_328,
            Sha256: "5047b556ce86ccaf6aa22b3ffccfc52d391ea4accdab9c2f2407da5b742d4363",
            Licence: ClipLicence),
        new ModelDescriptor(
            ModelId.ContentMerges,
            Version: 1,
            FileName: "clip_vit_l14_merges.txt",
            Bytes: 524_619,
            Sha256: "9fd691f7c8039210e0fced15865466c65820d09b63988b0174bfe25de299051a",
            Licence: ClipLicence),
    ]);

    public IReadOnlyCollection<ModelDescriptor> All => _byId.Values;

    public ModelDescriptor For(ModelId id) =>
        _byId.TryGetValue(id, out ModelDescriptor? descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(
                nameof(id), id, "The manifest describes no such model.");
}

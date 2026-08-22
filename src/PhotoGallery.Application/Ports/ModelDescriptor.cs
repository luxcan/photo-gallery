namespace PhotoGallery.Application.Ports;

/// <summary>
/// What a model file has to be before the app will use it.
/// </summary>
/// <remarks>
/// The descriptor is the claim; the digest is the truth - the same rule the
/// thumbnail store learned, where a row naming a rendition proved nothing about
/// the disk. A truncated <c>.onnx</c> does not announce itself: it fails deep
/// inside the runtime with a message that reads as the model being wrong rather
/// than the file being half there.
/// </remarks>
/// <param name="Version">
/// Bumped when the export changes. Vectors produced by one version do not
/// compare against another's, so a bump invalidates everything indexed with the
/// old file rather than leaving a library quietly returning worse answers.
/// </param>
public sealed record ModelDescriptor(
    ModelId Id,
    int Version,
    string FileName,
    long Bytes,
    string Sha256,
    string Licence);

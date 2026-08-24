using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Models;

/// <summary>
/// One model file as the screen that offers to install it needs to describe it.
/// </summary>
/// <param name="Licence">
/// Carried per file rather than per feature because it is the user's to accept,
/// and the two features do not answer to the same terms.
/// </param>
public sealed record ModelFileStatus(
    ModelId Id,
    string FileName,
    long Bytes,
    string Licence,
    ModelState State);

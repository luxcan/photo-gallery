namespace PhotoGallery.Application.Ports;

/// <summary>The executable a release carries, and how to prove a download of it.</summary>
/// <remarks>
/// Proof is not optional. A release with no digest, either GitHub's own or a
/// <c>.sha256</c> file published beside it, carries no download at all as far as
/// the app is concerned: it is offered as a page to read and nothing more,
/// because replacing a running program with bytes nobody can check is the one
/// thing an updater must never do.
/// </remarks>
/// <param name="Address">Where the executable is fetched from.</param>
/// <param name="Length">Its size in bytes, as the release states it.</param>
/// <param name="Sha256">
/// Its SHA-256 as lower-case hex, when the release states it directly.
/// </param>
/// <param name="Sha256Address">
/// Where a file holding the digest is, when the release states it that way
/// instead.
/// </param>
public sealed record ReleaseDownload(
    Uri Address,
    long Length,
    string? Sha256,
    Uri? Sha256Address);

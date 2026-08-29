namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// The faces one machine has found, and the models it found them with.
/// </summary>
/// <remarks>
/// The fingerprint travels with the vectors and not beside them, because it is
/// the only thing that makes them safe to accept. An embedding is meaningless
/// outside the model that produced it, and a mismatched one does not fail - it
/// returns a confident answer about the wrong person, which is worse than no
/// answer at all and looks exactly like a right one.
/// </remarks>
/// <param name="Models">
/// Each model this machine ran, by name and by the digest of its file. Compared
/// against this library's own before a single vector is read.
/// </param>
public sealed record FaceSet(
    MachineIdentity Machine,
    DateTime WrittenUtc,
    IReadOnlyDictionary<string, string> Models,
    IReadOnlyList<SharedFace> Faces)
{
    public static FaceSet Empty(MachineIdentity machine, DateTime writtenUtc) =>
        new(machine, writtenUtc, new Dictionary<string, string>(), []);
}

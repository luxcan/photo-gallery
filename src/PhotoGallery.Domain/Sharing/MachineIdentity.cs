namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Who published a decision set, and what it was published by.
/// </summary>
/// <remarks>
/// The versions are not decoration. A payload written by a newer release is
/// refused with a plain message rather than partially applied, because half a
/// schema applied is a library that looks fine and disagrees with itself.
/// </remarks>
/// <param name="Id">
/// Minted once by that machine and never reused. Row ids are local, so this is
/// the only thing two libraries can agree a machine is.
/// </param>
/// <param name="Name">What it calls itself. Editable there, and decides nothing.</param>
/// <param name="SchemaVersion">
/// What shape the decisions are in. Read before anything else in the payload.
/// </param>
public sealed record MachineIdentity(
    Guid Id,
    string Name,
    string AppVersion,
    int SchemaVersion);

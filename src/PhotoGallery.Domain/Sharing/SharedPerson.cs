namespace PhotoGallery.Domain.Sharing;

/// <summary>Somebody, as one machine tells another about them.</summary>
/// <param name="UpdatedUtc">
/// When the name was last typed, or null while it is the one they were first
/// given. Null loses to any date, so a name nobody has re-typed gives way to one
/// somebody has.
/// </param>
/// <param name="DeletedUtc">
/// When they were deleted, or null while they are still in the library. A
/// tombstone travels like anything else and is never expired: it is the only
/// record that somebody was deleted rather than never known.
/// </param>
public sealed record SharedPerson(
    Guid PublicId,
    string DisplayName,
    int? BirthYear,
    DateTime? UpdatedUtc,
    DateTime? DeletedUtc);

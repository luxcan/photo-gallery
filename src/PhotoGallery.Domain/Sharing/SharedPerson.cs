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
    DateTime? DeletedUtc)
{
    /// <summary>
    /// Them as they are, while they are here; once deleted, only who and when.
    /// </summary>
    /// <remarks>
    /// A deletion is somebody asking for a person to be forgotten, and a
    /// tombstone that went on carrying their name and birth year kept them in
    /// every library in the house and in the sync folder, written out again on
    /// every sync, for ever. The merge needs neither: it keys a person on
    /// <see cref="PublicId"/> and a tombstone wins on <see cref="DeletedUtc"/>
    /// alone. Applied to everything that arrives as well as everything that
    /// leaves, because a machine still on an older build goes on sending the
    /// name, and a merge that took it would write it straight back.
    /// </remarks>
    public SharedPerson ForgetIfDeleted() =>
        DeletedUtc is null ? this : this with { DisplayName = string.Empty, BirthYear = null };
}

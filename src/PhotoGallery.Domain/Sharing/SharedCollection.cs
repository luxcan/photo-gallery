namespace PhotoGallery.Domain.Sharing;

/// <summary>A shelf of albums, as one machine tells another about it.</summary>
/// <remarks>
/// The simplest thing in this payload, and deliberately so. A collection has no
/// photographs, no rule and no origin - everything on it is on it because an
/// album is - so there is nothing here to match against a library's contents and
/// nothing that can arrive before its pictures do. It is a name and two dates,
/// and it never waits.
///
/// <para>Which albums sit on it travels with the albums rather than here, for
/// the reason the column does: an album is on at most one shelf, and saying so
/// in one place is what keeps that true. See
/// <see cref="SharedAlbum.Shelf"/>.</para>
/// </remarks>
/// <param name="NamedUtc">
/// When somebody last typed the name. Not nullable as an album's is: a
/// collection cannot exist without a person naming it.
/// </param>
/// <param name="DeletedUtc">
/// When it was taken away, or null while it is still on the screen. A tombstone
/// rather than a removal, because a merge from a machine that still holds it
/// would otherwise put it back.
/// </param>
public sealed record SharedCollection(
    Guid PublicId,
    string Name,
    DateTime NamedUtc,
    DateTime? DeletedUtc);

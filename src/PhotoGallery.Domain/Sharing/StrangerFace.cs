namespace PhotoGallery.Domain.Sharing;

/// <summary>Somebody saying a face is nobody worth tracking.</summary>
/// <remarks>
/// Its own answer rather than a rejection, exactly as it is locally: a rejection
/// records that a face is not one particular person, which leaves it to be
/// offered as everybody else in turn. This says it is nobody, and it settles the
/// whole face - a confirmation and this one compete by date, because both are
/// answers a person gave.
///
/// <para>Un-marking is the gap, and a small one. Clearing the mark leaves no
/// date behind, so a face somebody let back in makes no claim and an old
/// "nobody" from another machine wins. In practice letting a face back in is
/// followed by naming it, and that name is dated and does win; the case left
/// over is a face un-marked and then left alone.</para>
/// </remarks>
public sealed record StrangerFace(FaceKey Face, DateTime DecidedUtc, Guid DecidedBy);

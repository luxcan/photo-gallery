using PhotoGallery.Domain.People;

namespace PhotoGallery.Domain.Sharing;

/// <summary>Somebody saying who is, or is not, in one face.</summary>
/// <param name="Source">
/// Which sort of answer it is. Proposals are not published - the other machine
/// will make its own, and better ones, from the confirmations it has just been
/// given - so in a payload this is a human answer.
/// </param>
/// <param name="DecidedUtc">
/// When it was decided, not when it was sent. It has to survive being passed on,
/// because a machine publishes everything it holds rather than only what it
/// decided itself.
/// </param>
/// <param name="DecidedBy">
/// The machine that decided it. Kept for the same reason, and used to settle a
/// tie so that three machines converge on one answer rather than on whichever
/// they heard last.
/// </param>
public sealed record FaceAnswer(
    FaceKey Face,
    Guid Person,
    AssignmentSource Source,
    DateTime DecidedUtc,
    Guid DecidedBy);

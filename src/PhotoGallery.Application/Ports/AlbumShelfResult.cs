namespace PhotoGallery.Application.Ports;

/// <summary>What happened when one album was told which collection it is on.</summary>
/// <param name="Left">
/// The collection the album came off, named, or null if it was on none. An
/// album is on at most one shelf, so choosing this one is leaving that one, and
/// a rule the user did not ask about must not be enforced in silence.
/// </param>
/// <param name="Kept">
/// Whether the album was a suggestion, and is now the user's. Putting a
/// proposal on a shelf is a person deciding it is worth keeping, so it is
/// accepted rather than left in the queue of questions - and that is a change
/// to somebody's library, which must be said rather than done quietly.
/// </param>
public sealed record AlbumShelfResult(string? Left, bool Kept)
{
    public static AlbumShelfResult Nothing { get; } = new(null, false);
}

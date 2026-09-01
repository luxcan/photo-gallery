namespace PhotoGallery.Application.Ports;

/// <summary>What happened when a collection was told which albums are on it.</summary>
/// <param name="Added">How many albums joined the shelf.</param>
/// <param name="Removed">
/// How many came off it. The tick list is the shelf rather than a way on to it,
/// so clearing a tick is how an album leaves, and the screen has to be able to
/// say that it did.
/// </param>
/// <param name="Kept">
/// How many of the albums that joined were suggestions, and were kept on the
/// way in. Putting a proposal on a shelf is a person deciding it is worth
/// keeping, so it is accepted rather than left in the queue of questions - and
/// that is a change to somebody's library, which must be said rather than done
/// quietly.
/// </param>
/// <param name="From">
/// The collections albums were taken off to get here, named. An album is on at
/// most one shelf, so ticking one that was on another moves it - and a rule the
/// user did not ask about must not be enforced in silence. The same answer the
/// photographs give when one is put into a second album.
/// </param>
public sealed record CollectionFillResult(
    int Added, int Removed, int Kept, IReadOnlyList<string> From)
{
    public static CollectionFillResult Nothing { get; } = new(0, 0, 0, []);
}

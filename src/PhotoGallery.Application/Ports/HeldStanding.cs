namespace PhotoGallery.Application.Ports;

/// <summary>
/// The answers waiting for their photographs, split by the only thing that
/// changes what anybody should do about them.
/// </summary>
/// <remarks>
/// One number was a lie. Thirty-eight answers were waiting on this library and
/// the screen said scanning would bring them in; thirty-seven named files that
/// had been renamed where they live and will never come back under those names,
/// and the last was a photograph sitting right there whose face this machine has
/// not found. Scanning would have brought in none of them, and the count could
/// never fall, so the line showed for ever and asked for something that could
/// not help.
///
/// <para>Counted rather than read. <see cref="IDecisionReader.WaitingAsync"/>
/// stays the dearer question: nine thousand held answers is nine thousand JSON
/// payloads to parse for a line of text nobody reads twice.</para>
/// </remarks>
/// <param name="Faces">
/// The photograph is indexed here; the face is not. Either the face pass has not
/// reached it, or this machine's detector found no box matching the one another
/// machine named. A scan may settle the first; nothing settles the second, and
/// the copy must not promise otherwise.
/// </param>
/// <param name="Here">
/// The photograph is indexed here and the answer is not about a face: a turn
/// waiting for a small copy of the photograph to turn, or a move waiting for an
/// album this library does not have. Its own group because every row on a
/// photograph that is here used to count as a face, and the sentence about the
/// face pass was then true of none of these. Between a scan indexing the
/// photograph and the same scan's last phase, any move, rejection or turn about
/// it is here too, waiting only for that phase - which a stopped scan never
/// reaches.
/// </param>
/// <param name="Missing">
/// The photograph is not indexed, but its folder is one this library has - so it
/// is not in that folder under that name. Renamed, moved, or not arrived. It may
/// come good; it may never, and neither is knowable from here.
/// </param>
/// <param name="Elsewhere">
/// Neither the photograph nor its folder is here: those pictures live on one
/// computer and nowhere else. The only group anybody can act on, and the one the
/// design answers with a folder everybody can reach.
/// </param>
/// <param name="Folder">
/// The one folder the <paramref name="Elsewhere"/> answers name, or empty where
/// they name more than one. Carried because "put that folder somewhere everybody
/// can reach" is an instruction, and an instruction with no folder in it is a
/// riddle.
/// </param>
public sealed record HeldStanding(
    HeldTally Faces,
    HeldTally Here,
    HeldTally Missing,
    HeldTally Elsewhere,
    string Folder)
{
    public static HeldStanding Nothing { get; } =
        new(HeldTally.None, HeldTally.None, HeldTally.None, HeldTally.None, string.Empty);

    /// <summary>Every held answer - what the single count used to be.</summary>
    public int Answers => Faces.Answers + Here.Answers + Missing.Answers + Elsewhere.Answers;
}

/// <summary>How much of one group there is, in both units the copy needs.</summary>
/// <remarks>
/// A photograph with eight faces in it holds eight answers, and one photograph
/// can hold rows of several kinds at once - a name, a turn and an album
/// membership are three rows about one picture. So the two numbers differ by a
/// lot and say different things: the answers are the evening's work at risk, the
/// photographs are what would have to turn up for it to be safe.
///
/// <para>Which is why the sentences use different units on purpose. The two
/// about work waiting count answers; the one asking somebody to move a folder
/// counts photographs, because a folder of photographs is the thing being
/// moved.</para>
/// </remarks>
public readonly record struct HeldTally(int Answers, int Photos)
{
    public static HeldTally None => default;
}

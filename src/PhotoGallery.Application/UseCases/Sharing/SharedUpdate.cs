namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Whether another computer has published answers this library has not taken.
/// </summary>
/// <remarks>
/// The question a person actually has - "is there anything for me?" - answered
/// without taking anything and without reading a single decision. A directory
/// listing already carries when each machine last wrote, and this library
/// already records when it last took from each of them, so the comparison costs
/// the listing and nothing else.
///
/// <para>It can be wrong in one direction and never in the other. A machine that
/// published without having decided anything still moves its file's date, so
/// this can say there is something when the merge will find nothing, which costs
/// one press. It cannot say there is nothing when there is something, because
/// taking answers is the only thing that moves the date it is compared
/// against.</para>
/// </remarks>
/// <param name="Machines">
/// The computers with something newer that this library has taken from before,
/// by the name it knows them by.
/// </param>
/// <param name="Unnamed">
/// How many more have something newer and have never been taken from. Counted
/// rather than named, because the name is inside a file this check deliberately
/// does not open - and counted rather than described, because a description in
/// the place of a name began the notice in lower case, and two computers
/// described alike were folded into one.
/// </param>
/// <param name="Newest">
/// When the newest of those files was written. What somebody who says "not now"
/// is saying it about: anything written after this is a new question.
/// </param>
public sealed record SharedUpdate(IReadOnlyList<string> Machines, int Unnamed, DateTime Newest)
{
    /// <summary>Nothing to take, which is the ordinary answer.</summary>
    public static SharedUpdate Nothing { get; } = new([], 0, DateTime.MinValue);

    public bool Any => Machines.Count > 0 || Unnamed > 0;
}

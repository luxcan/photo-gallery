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
/// The computers with something newer, by the name this library knows them by,
/// and described rather than named where it has never taken from them.
/// </param>
/// <param name="Newest">
/// When the newest of those files was written. What somebody who says "not now"
/// is saying it about: anything written after this is a new question.
/// </param>
public sealed record SharedUpdate(IReadOnlyList<string> Machines, DateTime Newest)
{
    /// <summary>Nothing to take, which is the ordinary answer.</summary>
    public static SharedUpdate Nothing { get; } = new([], DateTime.MinValue);

    public bool Any => Machines.Count > 0;
}

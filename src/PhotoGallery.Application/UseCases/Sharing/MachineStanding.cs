using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// One other computer in the house, and how recently it last shared.
/// </summary>
/// <param name="Name">
/// The name somebody here gave that machine, else what it calls itself, or a
/// description where this library has not taken its answers yet and so has
/// never been told.
/// </param>
/// <param name="SharedUtc">
/// When it last put its answers where this library could see them. Null for a
/// machine this library has heard of but which has shared nothing.
/// </param>
/// <param name="Merged">
/// Whether this library has actually taken its answers. A machine that has
/// shared but not been merged from is the ordinary state five seconds before
/// somebody presses the button, and is worth telling apart from one that has
/// nothing to offer.
/// </param>
public sealed record MachineStanding(string Name, DateTime? SharedUtc, bool Merged)
{
    /// <summary>The computer, by the id it gives itself.</summary>
    public Guid MachineId { get; init; }

    /// <summary>
    /// What somebody here said about taking its changes, or null for a
    /// computer whose file this library has never read and so cannot name.
    /// </summary>
    public MachineTrust? Trust { get; init; }

    /// <summary>
    /// Whether the screen should ask about it: read, named, and not answered
    /// for yet.
    /// </summary>
    public bool IsUnanswered => Trust == MachineTrust.Undecided;

    /// <summary>
    /// Whether another computer this library has heard of goes by the same
    /// name, whatever was said about either.
    /// </summary>
    /// <remarks>
    /// Told by the names the computers gave themselves and the names given
    /// them here, never by what the screen calls one it has not synced with
    /// yet - a description no file would ever claim.
    /// </remarks>
    public bool SharesItsName => NameItShares is not null;

    /// <summary>
    /// The name another computer here also goes by, or null where none does.
    /// </summary>
    public string? NameItShares { get; init; }

    /// <summary>
    /// What it calls itself, or null for a computer not written down here -
    /// which is also one that cannot be given a name of its own here yet.
    /// </summary>
    public string? OwnName { get; init; }

    /// <summary>The name somebody here gave it, or null where it has none.</summary>
    public string? FamiliarName { get; init; }

    /// <summary>Whether it can be given a name here.</summary>
    public bool CanBeNamed => OwnName is not null;

    /// <summary>
    /// How recently, as the screen says it.
    /// </summary>
    /// <remarks>
    /// Rounded hard and deliberately. Nobody needs "19 days ago" to the hour;
    /// what the line is for is telling a laptop that shared this morning from
    /// one that has been in a drawer since spring.
    /// </remarks>
    public string Recency(DateTime nowUtc)
    {
        if (SharedUtc is not DateTime shared)
        {
            return "never synced";
        }

        TimeSpan ago = nowUtc - shared;

        // A clock that is ahead reads as now rather than as a negative age. The
        // merge refuses a machine whose clock is far enough out and says so
        // properly; this line is not the place to raise it a second time.
        return ago switch
        {
            { TotalMinutes: < 60 } => "up to date",
            { TotalHours: < 24 } => Many((int)ago.TotalHours, "hour"),
            { TotalDays: < 14 } => Many((int)ago.TotalDays, "day"),
            { TotalDays: < 60 } => Many((int)(ago.TotalDays / 7), "week"),
            _ => Many((int)(ago.TotalDays / 30), "month"),
        };
    }

    private static string Many(int count, string unit) =>
        count <= 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
}

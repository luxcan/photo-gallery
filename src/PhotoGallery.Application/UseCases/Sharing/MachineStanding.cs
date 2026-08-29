namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// One other computer in the house, and how recently it last shared.
/// </summary>
/// <param name="Name">
/// What that machine calls itself, or a description where this library has not
/// taken its answers yet and so has never been told.
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
            return "never shared";
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

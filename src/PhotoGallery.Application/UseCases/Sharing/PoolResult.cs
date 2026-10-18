using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>What taking or giving the cached pictures did.</summary>
/// <param name="Filled">
/// Photographs this library gained without opening an original - the number the
/// whole second half of the feature exists to make large.
/// </param>
/// <param name="Fetched">
/// Renditions copied in. Fewer than <paramref name="Filled"/> wherever duplicate
/// rows share one picture, which on this library is four hundred sets of them.
/// </param>
/// <param name="Offered">Renditions copied out, for the machines that follow.</param>
/// <param name="Mismatched">
/// Photographs another machine has prepared at the same path whose bytes differ
/// from the file here. Prepared locally instead, and counted rather than hidden:
/// it separates "the pool had nothing for me" from "the two of you are looking
/// at different files".
/// </param>
public sealed record PoolResult(
    bool Ran,
    string Problem,
    int Filled,
    int Fetched,
    int Offered,
    int Mismatched,
    bool WasCancelled,
    int Faces = 0,
    IReadOnlyList<ModelMismatch>? Refused = null)
{
    public static PoolResult Nothing { get; } = new(true, string.Empty, 0, 0, 0, 0, false);

    /// <summary>A phase stopped before it had anything to count.</summary>
    public static PoolResult Stopped { get; } = new(true, string.Empty, 0, 0, 0, 0, true);

    public static PoolResult CouldNot(string problem) =>
        new(false, problem, 0, 0, 0, 0, false);

    /// <summary>Machines whose vectors could not be used, and which model differs.</summary>
    public IReadOnlyList<ModelMismatch> Mismatches => Refused ?? [];

    /// <summary>
    /// Whether this library's faces were written to the folder this time.
    /// </summary>
    /// <remarks>
    /// Only when they changed since the last time - the file is tens of
    /// megabytes, and a scan that found no new face has nothing new to say in
    /// it.
    /// </remarks>
    public bool FacesLeft { get; init; }

    /// <summary>
    /// The other machines this library's faces are waiting on, by name: ones
    /// whose copy of the app would add them a second time.
    /// </summary>
    public IReadOnlyList<string> FacesWaitFor { get; init; } = [];

    /// <summary>
    /// What landing the answers that were waiting did, where something arrived
    /// for them to land on - or null where nothing did.
    /// </summary>
    public HeldResult? Swept { get; init; }

    /// <summary>How long the phase took, folder questions and all.</summary>
    public TimeSpan Elapsed { get; init; }

    public bool ChangedNothing => Filled == 0 && Offered == 0;

    /// <summary>What to write in the scan's log.</summary>
    public string Summary
    {
        get
        {
            if (!Ran)
            {
                return Problem;
            }

            List<string> parts = [];

            if (Filled > 0)
            {
                parts.Add($"{Filled:N0} {(Filled == 1 ? "photo" : "photos")} filled in "
                        + "without reading the originals");
            }

            if (Offered > 0)
            {
                parts.Add($"{Offered:N0} put there for the other computers");
            }

            if (Faces > 0)
            {
                parts.Add($"{Faces:N0} faces taken instead of found again");
            }

            if (FacesLeft)
            {
                parts.Add("this computer's faces put there for the other computers");
            }

            if (Swept is { Applied: > 0 } swept)
            {
                parts.Add(swept.Applied == 1
                    ? "1 waiting answer applied"
                    : $"{swept.Applied:N0} waiting answers applied");
            }

            string done = parts.Count == 0
                ? "No pictures to copy"
                : string.Join(", ", parts);

            // Said because it is not a small result. A photograph at the same
            // path whose bytes differ means the two machines are looking at
            // different files, which is worth knowing and cannot be guessed at.
            string differing = Mismatched > 0
                ? $". {Mismatched:N0} {(Mismatched == 1 ? "photo differs" : "photos differ")} "
                + "from the copy on the other computer, so they are prepared here instead"
                : string.Empty;

            // Named rather than counted. "Some vectors were refused" is a
            // message nobody can act on; naming the machine and the model is one
            // somebody can go and fix in ten minutes.
            string models = Mismatches.Count == 0
                ? string.Empty
                : " " + string.Join(" ", Mismatches.Select(m => m.Explain()));

            string waiting = WaitingSentence();

            return WasCancelled
                ? $"Stopped - {done}{differing}. What was copied is copied; running this again "
                + $"carries on from here.{models}{waiting}"
                : done + differing + "." + models + waiting;
        }
    }

    /// <summary>
    /// Why this computer's faces are not in the folder yet, said once and
    /// plainly - or nothing, when they are not waiting on anybody.
    /// </summary>
    /// <remarks>
    /// Named rather than counted, like the model mismatch, because the remedy is
    /// a particular laptop: bring its copy of the app up to date and sync it
    /// once. Every copy says 1.0.0, so "an older version" would send somebody to
    /// a number that cannot tell them apart.
    /// </remarks>
    private string WaitingSentence()
    {
        if (FacesWaitFor.Count == 0)
        {
            return string.Empty;
        }

        string who = string.Join(" and ", FacesWaitFor);
        bool one = FacesWaitFor.Count == 1;

        return $" This computer's faces are not put there yet: {who} last synced from an "
             + "older copy of the app, which would add some faces twice. They follow once "
             + $"{(one ? "it has" : "they have")} synced from an up-to-date copy.";
    }
}

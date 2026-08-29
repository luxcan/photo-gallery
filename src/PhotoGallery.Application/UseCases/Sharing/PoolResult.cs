using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>What taking and giving the cached pictures did.</summary>
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

    public static PoolResult CouldNot(string problem) =>
        new(false, problem, 0, 0, 0, 0, false);

    /// <summary>Machines whose vectors could not be used, and which model differs.</summary>
    public IReadOnlyList<ModelMismatch> Mismatches => Refused ?? [];

    public bool ChangedNothing => Filled == 0 && Offered == 0;

    /// <summary>What to put on screen.</summary>
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
                parts.Add($"{Offered:N0} shared for the other computers");
            }

            if (Faces > 0)
            {
                parts.Add($"{Faces:N0} faces taken instead of found again");
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

            return WasCancelled
                ? $"Stopped - {done}{differing}. What was copied is copied; running this again "
                + $"carries on from here.{models}"
                : done + differing + "." + models;
        }
    }
}

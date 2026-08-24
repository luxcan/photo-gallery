using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Models;

namespace PhotoGallery.App.Models;

/// <summary>
/// One optional feature on the Settings screen: what it gives you, what it
/// costs, what to do to get it, and how much of it is here.
/// </summary>
/// <remarks>
/// The prose sits here rather than in the view, unlike the rest of this app,
/// because these are rows in a list and a template cannot carry a different
/// sentence per row. What belongs to the files themselves - their names, sizes
/// and licences - is not written here at all; it is read from the manifest, so
/// there is one statement of what a file has to be.
/// </remarks>
public sealed record FeatureCard(
    ModelFeature Feature,
    string Title,
    string Enables,
    string Steps,
    FeatureStatus Status)
{
    public static FeatureCard Of(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return status.Feature switch
        {
            ModelFeature.Faces => new FeatureCard(
                status.Feature,
                "Finding people",
                "Finds the faces in your photographs and lets you name the people in "
                + "them, once each. After that the People screen fills itself in, and "
                + "you can search your library by name.",
                "Press Download below and save both files into the folder in Step 1, "
                + "or click one file name to fetch just that one. They arrive under "
                + "the names shown, so there is nothing to rename and nothing to "
                + "unzip.",
                status),

            ModelFeature.ContentSearch => new FeatureCard(
                status.Feature,
                "Searching by what is in the picture",
                "Reads what each photograph is of, so you can search for \"birthday "
                + "cake\" or \"at the beach\" without having named or tagged anything.",

                // The collision is the whole reason this sentence is long. Two of
                // the four are called model.onnx where they come from, so a
                // browser saving both into one folder produces a "(1)" - and a
                // user who tidies that up by hand is as likely to overwrite one
                // as to fix it. Photo Gallery matches them by size and proves
                // them by digest, so the right answer is to leave them alone.
                "Press Download below and save all four files into the folder in Step "
                + "1, or click one file name to fetch just that one. Two of them "
                + "arrive called model.onnx, so your browser will name the second one "
                + "something like model (1).onnx - leave those names as they are. "
                + "Photo Gallery works out which is which and renames them itself.",
                status),

            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    /// <summary>The files themselves, so a half-finished job says which one is missing.</summary>
    public IReadOnlyList<ModelFileRow> FileRows => [.. Status.Files.Select(ModelFileRow.Of)];

    /// <summary>
    /// Everything this feature is still waiting for, so one press fetches the lot.
    /// </summary>
    /// <remarks>
    /// The link on each file name stays for the case of a single file gone
    /// astray, but four of them in a row are a procedure - and the ordinary
    /// answer to "I want this feature" should be one action, not four in the
    /// right order.
    /// </remarks>
    public IReadOnlyList<string> Downloads =>
        [.. FileRows.Where(row => row.CanDownload).Select(row => row.Url!)];

    /// <summary>Whether there is anything left to fetch.</summary>
    public bool CanDownload => Downloads.Count > 0;

    /// <summary>
    /// What that press will do, counted rather than left as "Download".
    /// </summary>
    /// <remarks>
    /// Counted because it says how many browser tabs are about to open, and
    /// because the count changes: coming back for the one file that failed
    /// should not offer to fetch all four again.
    /// </remarks>
    public string DownloadLabel
    {
        get
        {
            int wanted = Downloads.Count;

            if (wanted == Status.Files.Count)
            {
                return wanted == 2
                    ? "Download both files"
                    : $"Download all {wanted} files";
            }

            return wanted == 1
                ? "Download the remaining file"
                : $"Download the {wanted} remaining files";
        }
    }

    public bool IsReady => Status.IsReady;

    public bool IsPartial => Status.IsPartial;

    /// <summary>One of its files was there and was not what it claimed to be.</summary>
    public bool WasDamaged => Status.WasDamaged;

    /// <summary>Where this feature stands, in three words or so.</summary>
    public string StateCaption
    {
        get
        {
            if (Status.IsReady)
            {
                return "Installed";
            }

            if (Status.WasDamaged)
            {
                // The store deletes a file that fails its digest, so the folder
                // the user filled now has a hole in it. Saying only "not
                // installed" would read as the app having ignored them.
                return "One of these files was not what it claimed to be";
            }

            return Status.IsPartial
                ? $"{Status.Files.Count - Status.Outstanding.Count} of "
                  + $"{Status.Files.Count} files"
                : "Not installed";
        }
    }

    /// <summary>What it costs to download, or what is left to download.</summary>
    public string SizeCaption => Status.IsReady
        ? FileSize.Rounded(Status.Bytes)
        : Status.IsPartial
            ? $"{FileSize.Rounded(Status.OutstandingBytes)} still to download"
            : $"{FileSize.Rounded(Status.Bytes)} to download";

    /// <summary>
    /// The terms these files carry, said before the user is sent to fetch them.
    /// </summary>
    /// <remarks>
    /// Not a formality. The face weights are published for non-commercial
    /// research use and ship with no licence file, so the app cannot carry them
    /// and should not send anybody after them silently either.
    /// </remarks>
    public string Licence => string.Join(" ", Status.Licences);
}

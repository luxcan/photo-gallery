using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Shell;

/// <summary>
/// What is known about one picture, worded once for every screen that shows it.
/// </summary>
/// <remarks>
/// Three screens ask the same question about a photograph - the viewer, the face
/// review, and the duplicate comparison - and the answer has to read the same on
/// all of them. Formatting it in one place is what makes "4,470,593 bytes" mean
/// the same thing wherever it appears, and what stops one screen quietly
/// showing a file date as though it were a capture date.
/// </remarks>
public sealed partial class PhotoDetails
{
    /// <summary>
    /// Opens Explorer on the original, selected.
    /// </summary>
    /// <remarks>
    /// A command rather than a click handler, because this panel is a template
    /// in a resource dictionary and a dictionary has no code-behind to put a
    /// handler in.
    /// </remarks>
    [RelayCommand]
    private void ShowInExplorer() => OriginalInExplorer.Show(FullPath);

    private PhotoDetails(
        string fileName,
        string folderPath,
        string fullPath,
        long length,
        int? width,
        int? height,
        DateTime? takenUtc,
        DateTime modifiedUtc,
        string? contentHash,
        string? placeName = null,
        DateTime createdUtc = default)
    {
        FileName = fileName;
        FolderPath = folderPath;
        FullPath = fullPath;
        Length = length;
        Width = width;
        Height = height;
        TakenUtc = takenUtc;
        ModifiedUtc = modifiedUtc;
        ContentHash = contentHash;
        PlaceName = placeName;
        CreatedUtc = createdUtc;
    }

    public string FileName { get; }

    public string FolderPath { get; }

    public string FullPath { get; }

    public long Length { get; }

    public int? Width { get; }

    public int? Height { get; }

    public DateTime? TakenUtc { get; }

    public DateTime ModifiedUtc { get; }

    /// <summary>
    /// When the file was created, which on a library assembled by copying is the
    /// day it was copied rather than anything about the photograph.
    /// </summary>
    /// <remarks>
    /// Default when the caller does not know it - the duplicate comparison reads
    /// its copies from a query that never asked for it.
    /// </remarks>
    public DateTime CreatedUtc { get; }

    public string? ContentHash { get; }

    /// <summary>
    /// Where it was taken, or null when nothing is known about that.
    /// </summary>
    /// <remarks>
    /// Null rather than "Unknown", so the row can disappear. Most photographs
    /// have no coordinates and never will, and a permanently blank field beside
    /// a label reads as something the app failed to work out rather than
    /// something the camera never recorded.
    ///
    /// <para>Null on the duplicate comparison too, deliberately: two copies of
    /// one picture were taken in the same place, so it is never the reason to
    /// prefer one over the other and would only be a row repeated twice.</para>
    /// </remarks>
    public string? PlaceName { get; }

    public static PhotoDetails Of(PhotoFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return new PhotoDetails(
            facts.FileName, facts.FolderPath, facts.FullPath, facts.Length,
            facts.Width, facts.Height, facts.TakenUtc, facts.ModifiedUtc, facts.ContentHash,
            facts.PlaceName, facts.CreatedUtc);
    }

    /// <summary>
    /// The same panel for one copy of a duplicated picture, which already knows
    /// all of this without going back to the index for it.
    /// </summary>
    public static PhotoDetails Of(DuplicateCopy copy, string fileName, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return new PhotoDetails(
            fileName, folderPath, copy.FullPath, copy.Length,
            copy.Width, copy.Height, copy.TakenUtc, copy.ModifiedUtc, copy.ContentHash);
    }

    /// <summary>
    /// The size to read, and the size to compare: "4.3 MB (4,470,593 bytes)".
    /// </summary>
    /// <remarks>
    /// Both, not either. Bytes alone are unreadable at this scale - "80,088
    /// bytes" has to be counted before it means anything - but the rounded
    /// figure alone cannot be compared, and the duplicate screen puts two nearly
    /// identical copies side by side where the difference between them is the
    /// whole point. "4.3 MB" beside "4.3 MB" would hide the reason to prefer
    /// one.
    /// </remarks>
    public string ExactSize => Length < 1024
        ? $"{Length:N0} bytes"
        : $"{Rounded(Length)} ({Length:N0} bytes)";

    /// <summary>
    /// The size in the largest unit that leaves a number worth reading.
    /// </summary>
    /// <remarks>
    /// Binary units, because this is what the file system reports and what
    /// Explorer shows beside the same file - a KB that meant 1,000 here and
    /// 1,024 there would make the two disagree over every picture.
    /// </remarks>
    private static string Rounded(long bytes)
    {
        const long Kilobyte = 1024L;
        const long Megabyte = Kilobyte * 1024L;
        const long Gigabyte = Megabyte * 1024L;

        return bytes switch
        {
            >= Gigabyte => $"{bytes / (double)Gigabyte:N2} GB",
            >= Megabyte => $"{bytes / (double)Megabyte:N1} MB",
            _ => $"{bytes / (double)Kilobyte:N0} KB",
        };
    }

    public string Resolution => Width is int width && Height is int height
        ? $"{width:N0} x {height:N0}"
        : "not known yet";

    /// <summary>
    /// When the photograph was taken, or when the file was last written where
    /// the picture never said - and which of the two it is.
    /// </summary>
    /// <remarks>
    /// Never presented as the same thing. On a library assembled by copying, the
    /// file date is the day it was copied, and showing that as a capture date
    /// would be the app telling a small lie about someone's photographs.
    /// </remarks>
    public string When => TakenUtc is DateTime taken
        ? taken.ToLocalTime().ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture)
        : ModifiedUtc == default
            ? "not known"
            : ModifiedUtc.ToLocalTime().ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture)
              + "  (file date — the picture carries no capture date)";

    /// <summary>
    /// The same date, short enough to sit under a thumbnail.
    /// </summary>
    /// <remarks>
    /// Still says when it is only the file's date. Two copies of one picture
    /// commonly differ in exactly that - one carries the capture time and the
    /// re-saved one does not - and that is a reason to prefer a copy, so it
    /// cannot be the thing that gets trimmed for space.
    /// </remarks>
    public string WhenShort => TakenUtc is DateTime taken
        ? taken.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)
        : ModifiedUtc == default
            ? "date not known"
            : ModifiedUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)
              + "  (file date)";

    /// <summary>
    /// The whole-file digest, shortened to what a person can compare at a
    /// glance.
    /// </summary>
    /// <summary>The creation date as the file system reports it.</summary>
    public string Created => CreatedUtc == default
        ? "not recorded"
        : CreatedUtc.ToLocalTime().ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture);

    /// <summary>The modified date as the file system reports it.</summary>
    public string Modified => ModifiedUtc == default
        ? "not recorded"
        : ModifiedUtc.ToLocalTime().ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture);

    /// <summary>Whether either file date is worth showing at all.</summary>
    public bool HasFileDates => CreatedUtc != default || ModifiedUtc != default;

    /// <summary>
    /// Which date the app files this picture under, when that is not the one
    /// the reader would assume - and null when it is.
    /// </summary>
    /// <remarks>
    /// The rule is <c>AssetDates.BestGuess</c>: the capture date if there is
    /// one, otherwise the earlier of the two file dates. Only the fallback is
    /// worth a sentence. A picture with a capture date is already showing it
    /// under "Taken" two lines above, so saying it is filed under that date
    /// tells the reader something they can see - and it appeared on the great
    /// majority of pictures, which is the surest way to make a line stop being
    /// read at all.
    ///
    /// <para>The fallback is the opposite case and stays: it is very often not
    /// the creation date - copying stamps creation with the day of the copy and
    /// leaves the modified date alone, so 96% of the undated photographs here
    /// were created after they were last modified.</para>
    /// </remarks>
    public string? FiledUnder
    {
        get
        {
            if (TakenUtc is not null)
            {
                return null;
            }

            if (!HasFileDates)
            {
                return "No date at all, so it is filed at the end.";
            }

            bool createdIsEarlier = CreatedUtc != default && CreatedUtc < ModifiedUtc;
            return createdIsEarlier
                ? "No capture date, so it is filed under the created date - the earlier of the two."
                : "No capture date, so it is filed under the modified date - the earlier of the two.";
        }
    }

    public string Fingerprint => string.IsNullOrEmpty(ContentHash)
        ? "not taken yet"
        : ContentHash.Length <= 16 ? ContentHash : ContentHash[..16] + "...";
}

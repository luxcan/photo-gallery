using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Domain.Duplicates;

/// <summary>
/// Decides which copy of a duplicated file survives.
/// </summary>
/// <remarks>
/// This library stores many photos twice: once in a catch-all month folder
/// (<c>20230201</c>) and again in the folder that names the event
/// (<c>20230203 - Chingay</c>). A naive "shortest path wins" rule keeps the
/// meaningless copy, so the named folder is preferred first. Measured against
/// the real library, that reversed 218 of 362 decisions.
/// </remarks>
public static class KeeperPolicy
{
    /// <summary>
    /// A folder whose name is only a date carries no information beyond the date
    /// already on the file, e.g. <c>20230201</c> or <c>202302</c>.
    /// </summary>
    public static bool IsGenericFolder(string folderName)
    {
        if (folderName.Length is < 6 or > 8)
        {
            return false;
        }

        foreach (char c in folderName)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Picks the copy to keep: the best picture first, then the one in a
    /// descriptively named folder, then the shallower path, then ordinal path
    /// order so the choice is stable between runs.
    /// </summary>
    /// <exception cref="ArgumentException">The set is empty.</exception>
    public static Asset ChooseKeeper(IEnumerable<Asset> duplicates)
    {
        ArgumentNullException.ThrowIfNull(duplicates);

        Asset? best = null;
        foreach (Asset candidate in duplicates)
        {
            if (best is null || CompareQuality(candidate, best) < 0)
            {
                best = candidate;
            }
        }

        return best ?? throw new ArgumentException(
            "Cannot choose a keeper from an empty set.", nameof(duplicates));
    }

    /// <summary>
    /// Orders two copies by how good a picture each is, falling back to where
    /// they live when there is nothing to choose between them.
    /// </summary>
    /// <remarks>
    /// This does nothing at all for byte-identical copies - same bytes means
    /// same pixels and same length, so every quality comparison ties and the
    /// folder rule decides, exactly as it did before.
    ///
    /// <para>It matters for the visually alike, where the copies are genuinely
    /// different files. Measured on the real library, one set held a photograph
    /// at 6,099,490 bytes and a watermarked re-save of it at 4,488,377 in the
    /// same folder; on path order alone the app would have kept whichever name
    /// sorted first. More pixels, then more bytes, keeps the copy that has not
    /// been through a second encoder.</para>
    /// </remarks>
    public static int CompareQuality(Asset left, Asset right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        int byPixels = Pixels(right).CompareTo(Pixels(left));
        if (byPixels != 0)
        {
            return byPixels;
        }

        int byLength = right.Length.CompareTo(left.Length);
        return byLength != 0 ? byLength : ComparePreference(left, right);
    }

    /// <summary>
    /// How many pixels a copy holds, or zero where the dimensions were never
    /// recorded - an unknown size must not beat a known one.
    /// </summary>
    private static long Pixels(Asset asset) =>
        asset.Width is int width && asset.Height is int height
            ? (long)width * height
            : 0L;

    /// <summary>
    /// Orders two copies by preference. Negative means <paramref name="left"/>
    /// is the better copy to keep.
    /// </summary>
    public static int ComparePreference(Asset left, Asset right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        int byName = IsGenericFolder(left.TopFolder).CompareTo(IsGenericFolder(right.TopFolder));
        if (byName != 0)
        {
            return byName;
        }

        int byDepth = left.Depth.CompareTo(right.Depth);
        return byDepth != 0
            ? byDepth
            : string.CompareOrdinal(left.RelativePath, right.RelativePath);
    }
}

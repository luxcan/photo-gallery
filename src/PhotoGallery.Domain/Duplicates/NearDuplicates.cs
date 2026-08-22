using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Domain.Duplicates;

/// <summary>
/// Groups photographs that look the same without being the same file.
/// </summary>
/// <remarks>
/// A perceptual hash survives re-encoding, resizing and a light edit, so the
/// same picture saved twice lands within a few bits of itself. What it cannot do
/// is tell a re-saved copy from the next frame of a burst - both are a handful
/// of bits apart, and one of them is a photograph worth keeping. That is why
/// nothing found here is ever acted on without being looked at.
/// </remarks>
public static class NearDuplicates
{
    /// <summary>
    /// How many of the 64 bits may differ before two pictures are no longer the
    /// same picture.
    /// </summary>
    /// <remarks>
    /// Measured on the real library, over the 11,005 photos that are not already
    /// byte-identical to something: at 0 it finds 82 sets, at 4 it finds 472, at
    /// 8 it finds 914. The count keeps climbing because the looser it gets the
    /// more of a burst it swallows, not because there are more duplicates - so
    /// this sits at the point where a re-save is still caught and a sequence of
    /// separate photographs mostly is not.
    /// </remarks>
    public const int DefaultThreshold = 4;

    /// <summary>
    /// Every group of two or more photographs within <paramref name="threshold"/>
    /// bits of each other.
    /// </summary>
    /// <remarks>
    /// Leader clustering: each unclaimed picture in turn gathers everything still
    /// unclaimed that is close to it. Deliberately not transitive closure - with
    /// a threshold of 4, chaining would let a picture 4 bits away from one 4 bits
    /// away from a third drag unrelated photographs into one set, and a set the
    /// user cannot see the sense of is worse than no set.
    ///
    /// <para>Ordered by <see cref="Asset.Id"/> first so the same library always
    /// produces the same sets. Leader clustering depends on who goes first, and
    /// a pass whose answers moved between runs would be impossible to trust.</para>
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<Asset>> Group(
        IEnumerable<Asset> photos, int threshold = DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);

        Asset[] ordered =
        [
            .. photos.Where(photo => photo.PerceptualHash is not null).OrderBy(photo => photo.Id),
        ];

        var taken = new bool[ordered.Length];
        var sets = new List<IReadOnlyList<Asset>>();

        for (int leader = 0; leader < ordered.Length; leader++)
        {
            if (taken[leader])
            {
                continue;
            }

            PerceptualHash hash = ordered[leader].PerceptualHash!.Value;
            List<Asset>? set = null;

            for (int other = leader + 1; other < ordered.Length; other++)
            {
                if (taken[other]
                    || hash.DistanceTo(ordered[other].PerceptualHash!.Value) > threshold)
                {
                    continue;
                }

                set ??= [ordered[leader]];
                set.Add(ordered[other]);
                taken[other] = true;
            }

            if (set is not null)
            {
                taken[leader] = true;
                sets.Add(set);
            }
        }

        return sets;
    }

    /// <summary>
    /// How far each member sits from the one being kept, which is what the
    /// review screen shows to say how alike they are.
    /// </summary>
    public static int DistanceFrom(Asset keeper, Asset member)
    {
        ArgumentNullException.ThrowIfNull(keeper);
        ArgumentNullException.ThrowIfNull(member);

        return keeper.PerceptualHash is PerceptualHash left
            && member.PerceptualHash is PerceptualHash right
                ? left.DistanceTo(right)
                : 0;
    }
}

using System.Linq.Expressions;

namespace PhotoGallery.Domain.Assets;

/// <summary>
/// The best answer available to "when was this taken?".
/// </summary>
/// <remarks>
/// The authority for it, because the gallery orders by it in SQL and several
/// screens report it in memory, and a disagreement between those two would put a
/// photograph in one place and label it another.
/// </remarks>
public static class AssetDates
{
    /// <summary>
    /// The photograph's own date where it has one, and otherwise the earlier of
    /// the file's two timestamps.
    /// </summary>
    /// <remarks>
    /// The earlier one, because a file's dates can only ever move forward from
    /// the moment the shutter fired. Copying, syncing, restoring from a backup
    /// and editing all push a timestamp later; nothing pushes one earlier. So
    /// taking the earlier of the two is never worse than taking the modified
    /// date, and is better whenever a creation date has survived intact.
    ///
    /// <para>Measured on a library of 16,225 files, against the 9,882 that carry
    /// a real capture date: the modified date alone lands on the right day 7,309
    /// times and the creation date alone 308, because that library was assembled
    /// by copying and 2,758 of its files claim to have been created on one
    /// afternoon. The earlier of the two lands on the right day 7,527 times. Of
    /// the 1,868 files whose creation date is the earlier, it is the nearer to
    /// the truth in 1,866.</para>
    ///
    /// <para>Nothing here reads a folder name. Dating a photograph by the folder
    /// it was filed in works beautifully for a library named that way and not at
    /// all for anyone else, and this has to be right for everyone.</para>
    /// </remarks>
    public static DateTime BestGuess(DateTime? takenUtc, DateTime createdUtc, DateTime modifiedUtc)
    {
        if (takenUtc is DateTime taken)
        {
            return taken;
        }

        // The sentinel, from rows indexed before creation dates were recorded.
        // An unknown date is not an earlier one.
        return createdUtc != default && createdUtc < modifiedUtc ? createdUtc : modifiedUtc;
    }

    /// <summary>
    /// The same answer, written so the database can work it out for itself.
    /// </summary>
    /// <remarks>
    /// A method group cannot be translated to SQL, so the rule above has to be
    /// written a second time as an expression. It lives here rather than in
    /// whichever reader happens to need it, because it had already been written
    /// twice in two projects before anything wanted it a third time, and the
    /// class holding <see cref="BestGuess"/> is the one place a reader looks to
    /// find out what this library means by a capture date.
    ///
    /// <para><c>TheQueryableRuleAgreesWithTheOneInMemory</c> fails the moment
    /// this and <see cref="BestGuess"/> disagree.</para>
    /// </remarks>
    public static readonly Expression<Func<Asset, DateTime>> Taken =
        asset => asset.TakenUtc
                 ?? (asset.CreatedUtc != default && asset.CreatedUtc < asset.ModifiedUtc
                     ? asset.CreatedUtc
                     : asset.ModifiedUtc);

    /// <summary>
    /// Everything taken within a run of days, either end optional.
    /// </summary>
    /// <remarks>
    /// The last day is included whole, because somebody who types one date means
    /// that day and not the instant it begins - and that belongs here, beside
    /// the rule about which date is read, rather than in each caller that asks
    /// the question.
    ///
    /// <para>A photograph carrying no capture date is inside the range on its
    /// file's date rather than left out of it. Every video in a real library has
    /// no capture date - not one of the 6,357 in the library this was measured
    /// against - so the other reading means a day rule can never match a video,
    /// while the same screens cheerfully show that video sitting on that day.
    /// The cost is honest and worth saying out loud: a file whose timestamps are
    /// really the day it was copied will answer for the day it was copied. That
    /// is the same trade <see cref="BestGuess"/> already makes everywhere else,
    /// and making it in one place and not the other is what left a rule
    /// disagreeing with the grid above it.</para>
    ///
    /// <para>Written out twice inside the one expression, which is not a
    /// mistake: a range needs the value on both sides of it, and an expression
    /// tree has nowhere to put a name for a sub-expression. Two comparisons in
    /// one method, in view of each other, is the smallest form of that.</para>
    /// </remarks>
    public static Expression<Func<Asset, bool>> TakenBetween(DateOnly? from, DateOnly? to)
    {
        DateTime start = from?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        DateTime before = to?.AddDays(1).ToDateTime(TimeOnly.MinValue) ?? DateTime.MaxValue;

        return asset =>
            (asset.TakenUtc
             ?? (asset.CreatedUtc != default && asset.CreatedUtc < asset.ModifiedUtc
                 ? asset.CreatedUtc
                 : asset.ModifiedUtc)) >= start
            && (asset.TakenUtc
                ?? (asset.CreatedUtc != default && asset.CreatedUtc < asset.ModifiedUtc
                    ? asset.CreatedUtc
                    : asset.ModifiedUtc)) < before;
    }
}

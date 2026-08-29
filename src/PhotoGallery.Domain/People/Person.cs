namespace PhotoGallery.Domain.People;

/// <summary>Someone who appears in the library, once you have named them.</summary>
public sealed class Person
{
    public int Id { get; set; }

    /// <summary>
    /// Who this is, on every machine that has been told about them.
    /// </summary>
    /// <remarks>
    /// Minted when the person is created and never reused. Row ids are local and
    /// meaningless across machines, so this is what lets one library say "the
    /// person you called Ana" to another.
    ///
    /// <para>Two machines that each created "Ana" independently produce two of
    /// these, and they stay apart: two Anas is a real thing in a family, and
    /// joining them on the strength of a matching name would be a merge deciding
    /// something only a person can. They are offered as a join instead, where
    /// their eras agree.</para>
    ///
    /// <para>Minted where the property is declared rather than by whoever happens
    /// to construct one. Three unique columns arrived with sharing, and a call
    /// site that forgot would not fail on its own row - it would fail on the
    /// second row anybody added, having already written a first that claims the
    /// same identity as everything else in the library. An identity is a fact
    /// about the thing, not a step in making one.</para>
    ///
    /// <para>Safe on the way back in: this is a mapped property, so a row read
    /// from the database overwrites it with the identity it was stored
    /// with.</para>
    /// </remarks>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    public required string DisplayName { get; set; }

    /// <summary>
    /// When somebody last typed this name, or null while it is still the one they
    /// were first given.
    /// </summary>
    /// <remarks>
    /// Which of two names for one person wins. Null loses to any date, which is
    /// what makes the answer right for every library that predates sharing: a
    /// name nobody has re-typed since gives way to one somebody has, and two that
    /// have never been re-typed are either the same name or belong to two
    /// different people.
    /// </remarks>
    public DateTime? UpdatedUtc { get; set; }

    /// <summary>
    /// When this person was deleted, or null while they are still in the library.
    /// </summary>
    /// <remarks>
    /// A tombstone, and the only thing standing between a deletion and the next
    /// merge from anybody who still holds them. Without it a person deleted here
    /// is quietly restored, and then propagates.
    ///
    /// <para><strong>Kept for ever.</strong> Tidying rows that only accumulate is
    /// an obvious thing to write and would be wrong here: this is the sole record
    /// that somebody was deleted rather than never known, so an expiry lets a
    /// deleted person walk back in from the next machine that still has them.
    /// Fifteen people is not a table that needs tidying.</para>
    /// </remarks>
    public DateTime? DeletedUtc { get; set; }

    /// <summary>
    /// The year they were born, when it has been given. Optional: most people in
    /// a family library are recognisable without one, and only the ones the user
    /// cares to date get an age.
    /// </summary>
    public int? BirthYear { get; set; }

    public List<PersonEra> Eras { get; } = [];

    /// <summary>
    /// How old they were in the year a picture was taken, or <c>null</c> when no
    /// birth year has been given.
    /// </summary>
    public int? AgeAt(DateTime takenUtc) => PersonAge.At(BirthYear, takenUtc);

    /// <summary>
    /// The era covering a given date, or the nearest one when the date falls
    /// outside every era - a photo from before the first era should still be
    /// compared against the earliest appearance rather than nothing at all.
    /// </summary>
    public PersonEra? EraFor(DateTime takenUtc)
    {
        if (Eras.Count == 0)
        {
            return null;
        }

        foreach (PersonEra era in Eras)
        {
            if (era.Covers(takenUtc))
            {
                return era;
            }
        }

        PersonEra nearest = Eras[0];
        long nearestGap = GapTo(nearest, takenUtc);
        foreach (PersonEra era in Eras)
        {
            long gap = GapTo(era, takenUtc);
            if (gap < nearestGap)
            {
                nearest = era;
                nearestGap = gap;
            }
        }

        return nearest;
    }

    private static long GapTo(PersonEra era, DateTime takenUtc)
    {
        if (takenUtc < era.FromUtc)
        {
            return (era.FromUtc - takenUtc).Ticks;
        }

        return takenUtc >= era.ToUtc ? (takenUtc - era.ToUtc).Ticks : 0L;
    }
}

namespace PhotoGallery.Domain.People;

/// <summary>Someone who appears in the library, once you have named them.</summary>
public sealed class Person
{
    public int Id { get; set; }

    public required string DisplayName { get; set; }

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

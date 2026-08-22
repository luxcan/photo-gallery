namespace PhotoGallery.Domain.People;

/// <summary>How old somebody was when a picture was taken.</summary>
/// <remarks>
/// <para>
/// A year is all that is asked for, so a year is all that can be answered. The
/// number here is the age they reach during that calendar year, not their age on
/// the day: somebody born in June 2015 counts as 1 for the whole of 2016,
/// including the five months of it they were still nought. Only a full birth
/// date could close that gap, so the screen states the convention once rather
/// than claiming a precision it has not got.
/// </para>
/// <para>
/// Age nought is written "Under 1" instead. Within the calendar year of their
/// birth a person is necessarily under one, so that label is the one heading
/// that cannot be off by a year - and it is also where being wrong would show
/// most, because it is the babies whose months are the whole point.
/// </para>
/// <para>
/// A picture dated before the year of birth is not an age of minus three. It is
/// a date the app has got wrong, almost always a file date standing in for a
/// capture date that was never written. The difference is returned as it is so
/// the caller can set those aside together rather than scattering them.
/// </para>
/// </remarks>
public static class PersonAge
{
    /// <summary>The earliest year taken as a year of birth rather than a slip.</summary>
    /// <remarks>
    /// A personal photo library, not a genealogy. This is here to catch a typed
    /// "215" or "20155", not to rule on how long people live.
    /// </remarks>
    public const int EarliestYear = 1900;

    /// <summary>The bucket every date before the year of birth shares.</summary>
    public const int BeforeBirth = -1;

    /// <summary>
    /// The age reached in the year a picture is dated to, or <c>null</c> when no
    /// year of birth has been given. Negative when the picture claims to predate
    /// the birth.
    /// </summary>
    public static int? At(int? birthYear, DateTime dated) =>
        birthYear is int born ? dated.Year - born : null;

    /// <summary>
    /// The bucket an age belongs in: itself, or <see cref="BeforeBirth"/> for
    /// everything dated before the birth, so those gather in one place rather
    /// than forming a group per impossible year.
    /// </summary>
    public static int Bucket(int age) => age < 0 ? BeforeBirth : age;

    /// <summary>What a group of one age is called.</summary>
    public static string Heading(int bucket) => bucket switch
    {
        BeforeBirth => "Dated before they were born",
        0 => "Under 1",
        _ => $"Age {bucket}",
    };

    /// <summary>Whether a year could be somebody's year of birth.</summary>
    public static bool IsPlausible(int birthYear, DateTime today) =>
        birthYear >= EarliestYear && birthYear <= today.Year;
}

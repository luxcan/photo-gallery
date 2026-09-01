using System.Globalization;

namespace PhotoGallery.Domain.Albums;

/// <summary>
/// What to call an occasion, from what is actually known about it.
/// </summary>
/// <remarks>
/// A ladder rather than a formula, because most rungs are unavailable most of
/// the time: coordinates are on about one photograph in nine in a real library,
/// so the place rungs are the exception and the date rung is the normal case.
/// It has to be a name somebody is content to see.
///
/// <para><strong>Never invent.</strong> Every rung states something the index
/// can prove - a place that was resolved, a person the user named themselves, a
/// month. "Weekend" is only used when the days really are a Saturday and a
/// Sunday. If the only honest name is a month and a count, that is the name.</para>
/// </remarks>
public static class AlbumNamer
{
    /// <summary>The title for an occasion. The span and the count are shown beside it.</summary>
    public static string Name(AlbumFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.Places.Count > 0)
        {
            string where = Join(facts.Places);

            // Only a trip when the clusterer measured it as one. Otherwise
            // every weekend at home becomes a journey.
            return facts.Kind == AlbumKind.Trip ? $"{where} Trip" : where;
        }

        if (facts.People.Count > 0)
        {
            return $"{Span(facts)} with {Join(facts.People)}";
        }

        // The bottom rung, and the one most albums land on. It says which
        // days rather than only which month: a fortnight of daily photographs
        // that the cap breaks apart would otherwise produce eleven albums
        // all called "September 2019", indistinguishable in a list.
        //
        // Formatted from the wall-clock value the camera wrote - never
        // converted, because a capture time carries no offset and "converting"
        // it shifts the name by whatever the machine's timezone happens to be.
        return Dates(facts);
    }

    /// <summary>
    /// The days themselves, as a person writes them.
    /// </summary>
    private static string Dates(AlbumFacts facts)
    {
        DateTime start = facts.StartUtc;
        DateTime end = facts.EndUtc;
        CultureInfo culture = CultureInfo.CurrentCulture;

        if (start.Date == end.Date)
        {
            return start.ToString("d MMMM yyyy", culture);
        }

        // Within one month the month and year are said once: "1-6 September
        // 2019" rather than "1 September 2019 - 6 September 2019".
        return start.Year == end.Year && start.Month == end.Month
            ? $"{start.Day}-{end.ToString("d MMMM yyyy", culture)}"
            : $"{start.ToString("d MMMM", culture)} - {end.ToString("d MMMM yyyy", culture)}";
    }

    /// <summary>How long it lasted, in the words a person would use.</summary>
    private static string Span(AlbumFacts facts)
    {
        int days = facts.Days;
        if (days == 1)
        {
            return "A day";
        }

        return days <= 3 && CoversAWeekend(facts) ? "A weekend" : $"{days} days";
    }

    /// <summary>
    /// Whether the span really includes a Saturday and a Sunday.
    /// </summary>
    /// <remarks>
    /// Checked rather than assumed from the length. A Tuesday and a Wednesday
    /// away is two days, and calling it a weekend is the kind of small
    /// invention that makes a user stop trusting the names.
    /// </remarks>
    private static bool CoversAWeekend(AlbumFacts facts)
    {
        bool saturday = false;
        bool sunday = false;

        for (DateTime day = facts.StartUtc.Date; day <= facts.EndUtc.Date; day = day.AddDays(1))
        {
            saturday |= day.DayOfWeek == DayOfWeek.Saturday;
            sunday |= day.DayOfWeek == DayOfWeek.Sunday;
        }

        return saturday && sunday;
    }

    /// <summary>
    /// Two names at most, because a title is not a list.
    /// </summary>
    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{names[0]}, {names[1]} and others",
    };
}

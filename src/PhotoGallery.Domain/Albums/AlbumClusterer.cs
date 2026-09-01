using PhotoGallery.Domain.Places;

namespace PhotoGallery.Domain.Albums;

/// <summary>
/// Groups photographs into occasions, from their capture times and where they
/// were taken.
/// </summary>
/// <remarks>
/// No model, no weights: two thresholds and a sort. Apple and Google build the
/// same feature the same way - group on time and place first, then label the
/// group with who and what is in it.
///
/// <para><strong>Why two levels.</strong> The obvious rule is one gap: start a
/// new album after six hours with no photographs. Simulated over 9,544
/// dated photographs in a real library, that produced 180 albums and not
/// one of them spanned more than a day - because everybody sleeps, and a
/// night's gap ends the group. A three-day trip came out as three separate
/// days. Widening the single threshold is worse rather than better: at
/// twenty-four hours one album swallowed twenty-five days.
///
/// So a <em>session</em> is photographs no more than six hours apart, and a
/// <em>album</em> is a run of sessions on consecutive days. A day with no
/// photographs ends the run. On the same library that produces 243 albums,
/// 178 of them spanning more than one day.</para>
///
/// <para><strong>Why the cap.</strong> Somebody who photographs something every
/// day has runs that never end - the longest in that library is 63 consecutive
/// days. That is not an occasion, it is ordinary life, so a run longer than
/// three weeks is offered as its separate days instead.</para>
///
/// <para>Pure, and deliberately so: everything it needs is passed in, so the
/// rules can be tested against a handful of timestamps rather than a library.</para>
/// </remarks>
public static class AlbumClusterer
{
    /// <summary>A gap longer than this ends a session.</summary>
    /// <remarks>
    /// Six hours: a night's sleep separates two days of a trip; lunch does not.
    /// </remarks>
    public static readonly TimeSpan SessionGap = TimeSpan.FromHours(6);

    /// <summary>How long a run of consecutive days may be and still be an occasion.</summary>
    /// <remarks>
    /// Three weeks, chosen by measurement rather than by taste. Ten was the
    /// first guess and a fortnight away came back as eleven separate days;
    /// fourteen still broke a fifteen-day run apart. Over the whole library:
    ///
    /// <list type="bullet">
    /// <item>14 days splits 5 runs, 148 days between them;</item>
    /// <item>21 days splits 3 runs, and the longest occasion kept whole is 20 days;</item>
    /// <item>28 days splits only 1 - but calls a 26-day stretch an occasion,
    /// which it is not.</item>
    /// </list>
    ///
    /// Twenty-one keeps every plausible holiday whole and still catches the case
    /// this exists for: the longest uncapped run measured is 63 consecutive
    /// days, which is somebody photographing something daily rather than a
    /// journey.
    /// </remarks>
    public const int LongestRunDays = 21;

    /// <summary>Fewer than this and it is a handful of shots, not an occasion.</summary>
    public const int FewestPhotos = 8;

    /// <summary>Shorter than this and a single burst would become an album.</summary>
    public static readonly TimeSpan ShortestSpan = TimeSpan.FromMinutes(90);

    /// <summary>
    /// How far from the usual place an album has to be to be called a trip.
    /// </summary>
    /// <remarks>
    /// Far enough that it is somewhere else, loose enough that a day out stays
    /// one place.
    /// </remarks>
    public const double AwayKilometres = 50d;

    /// <summary>
    /// How many photographs must carry coordinates before their middle is
    /// treated as home.
    /// </summary>
    /// <remarks>
    /// A library with three coordinates has no usual place, and calling their
    /// midpoint "home" would make a trip of everything that is not those three.
    /// </remarks>
    private const int FewestForHome = 20;

    /// <summary>
    /// The occasions in these photographs, in the order they happened.
    /// </summary>
    /// <param name="photos">
    /// Everything that could be grouped. Photographs with no capture date are
    /// the caller's to leave out: this cannot place them on a timeline, and
    /// guessing puts them in whichever group they land beside.
    /// </param>
    public static IReadOnlyList<PhotoGroup> Group(IReadOnlyList<DatedPhoto> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);

        if (photos.Count == 0)
        {
            return [];
        }

        List<DatedPhoto> ordered =
            [.. photos.OrderBy(photo => photo.TakenUtc).ThenBy(photo => photo.AssetId)];

        (double Latitude, double Longitude)? home = UsualPlace(ordered);

        var groups = new List<PhotoGroup>();
        foreach (List<DatedPhoto> run in Runs(Sessions(ordered)))
        {
            int days = Days(run);
            if (days > LongestRunDays)
            {
                // Ordinary life rather than an occasion. Its days are still
                // worth offering one at a time - a good day inside a long
                // stretch is still a good day.
                foreach (IGrouping<DateOnly, DatedPhoto> day in run.GroupBy(DayOf))
                {
                    Keep(groups, [.. day], home, AlbumKind.Period);
                }

                continue;
            }

            Keep(groups, run, home, days > 1 ? AlbumKind.Event : AlbumKind.Day);
        }

        return groups;
    }

    /// <summary>Photographs no more than <see cref="SessionGap"/> apart.</summary>
    private static List<List<DatedPhoto>> Sessions(List<DatedPhoto> ordered)
    {
        var sessions = new List<List<DatedPhoto>>();
        var current = new List<DatedPhoto> { ordered[0] };

        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].TakenUtc - ordered[i - 1].TakenUtc > SessionGap)
            {
                sessions.Add(current);
                current = [];
            }

            current.Add(ordered[i]);
        }

        sessions.Add(current);

        return sessions;
    }

    /// <summary>Sessions on consecutive days, joined. A quiet day ends the run.</summary>
    private static List<List<DatedPhoto>> Runs(List<List<DatedPhoto>> sessions)
    {
        var runs = new List<List<DatedPhoto>>();
        var current = new List<DatedPhoto>(sessions[0]);

        for (int i = 1; i < sessions.Count; i++)
        {
            DateOnly lastDay = DayOf(current[^1]);
            DateOnly nextDay = DayOf(sessions[i][0]);

            if (nextDay.DayNumber - lastDay.DayNumber > 1)
            {
                runs.Add(current);
                current = [];
            }

            current.AddRange(sessions[i]);
        }

        runs.Add(current);

        return runs;
    }

    /// <summary>Adds the group if it earns its place, named by what it is.</summary>
    private static void Keep(
        List<PhotoGroup> groups,
        List<DatedPhoto> photos,
        (double Latitude, double Longitude)? home,
        AlbumKind kind)
    {
        if (photos.Count < FewestPhotos)
        {
            return;
        }

        DateTime start = photos[0].TakenUtc;
        DateTime end = photos[^1].TakenUtc;

        if (end - start < ShortestSpan)
        {
            return;
        }

        groups.Add(new PhotoGroup(
            [.. photos.Select(photo => photo.AssetId)],
            start,
            end,
            IsAway(photos, home) ? AlbumKind.Trip : kind));
    }

    /// <summary>
    /// Whether these photographs sit far enough from the usual place to be
    /// somewhere else.
    /// </summary>
    /// <remarks>
    /// Unknown counts as not away. Coordinates are on about one photograph in
    /// nine in a real library, so most albums cannot answer this, and
    /// calling them all trips on no evidence is exactly what the rule exists to
    /// prevent.
    /// </remarks>
    private static bool IsAway(
        List<DatedPhoto> photos, (double Latitude, double Longitude)? home)
    {
        if (home is not (double homeLatitude, double homeLongitude))
        {
            return false;
        }

        (double Latitude, double Longitude)? middle = Middle(photos);

        return middle is (double latitude, double longitude)
            && Coordinates.Kilometres(latitude, longitude, homeLatitude, homeLongitude)
               > AwayKilometres;
    }

    /// <summary>Where this library's photographs usually are, or null if too few say.</summary>
    private static (double Latitude, double Longitude)? UsualPlace(List<DatedPhoto> photos) =>
        photos.Count(photo => photo.HasPlace) < FewestForHome ? null : Middle(photos);

    /// <summary>
    /// The middle of whatever coordinates these carry.
    /// </summary>
    /// <remarks>
    /// The median of each side rather than the mean: one photograph taken on a
    /// stopover drags a mean halfway across a country, and the question being
    /// asked is where the bulk of them are.
    /// </remarks>
    private static (double Latitude, double Longitude)? Middle(List<DatedPhoto> photos)
    {
        double[] latitudes = [.. photos.Where(p => p.HasPlace).Select(p => p.Latitude!.Value).Order()];
        if (latitudes.Length == 0)
        {
            return null;
        }

        double[] longitudes =
            [.. photos.Where(p => p.HasPlace).Select(p => p.Longitude!.Value).Order()];

        return (latitudes[latitudes.Length / 2], longitudes[longitudes.Length / 2]);
    }

    private static DateOnly DayOf(DatedPhoto photo) => DateOnly.FromDateTime(photo.TakenUtc);

    private static int Days(List<DatedPhoto> run) =>
        DayOf(run[^1]).DayNumber - DayOf(run[0]).DayNumber + 1;
}

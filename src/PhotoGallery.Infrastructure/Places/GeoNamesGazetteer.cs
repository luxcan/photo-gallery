using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Places;

namespace PhotoGallery.Infrastructure.Places;

/// <summary>
/// The nearest populated place to a coordinate, from GeoNames' own data.
/// </summary>
/// <remarks>
/// 235,403 places, compiled into this assembly rather than installed beside it.
/// GeoNames publishes 38.8 MB of tab-separated text, most of it the
/// <c>alternatenames</c> column - every local spelling of every place, which
/// this app never reads. Trimmed to the six fields below and compressed it is
/// 2.8 MB, which is small enough that place names need no download, no install
/// step and no setup: they work on a library's first run, with an empty working
/// folder and no network. See <c>docs/gazetteer.md</c>.
///
/// <para>Read once and held for as long as this instance lives, and loaded
/// lazily - a library with no coordinates in it never pays the ~450 ms.</para>
///
/// <para><b>Why a grid rather than a scan.</b> Comparing every place against
/// every photograph is 235,403 distances each. Over the 6,300 photographs this
/// library expects to have coordinates that is 1.5 billion, which is minutes of
/// pure arithmetic for an answer that never changes. Bucketed by whole degree,
/// a lookup examines the handful of cells that could possibly hold the winner -
/// a few hundred candidates - and the pass stops being measurable.</para>
///
/// <para><b>Why more than one ring of cells.</b> A degree of latitude is 111 km
/// everywhere, but a degree of longitude shrinks towards the poles - 111 km at
/// the equator, 19 km at 80 degrees north. A fixed three-by-three block is
/// therefore not a fixed distance, and near the poles it would be narrower than
/// the search radius, quietly missing the nearest place. The number of columns
/// is computed from the latitude instead.</para>
/// </remarks>
public sealed class GeoNamesGazetteer : IGeocoder
{
    /// <summary>
    /// How far away a place may be and still name a photograph.
    /// </summary>
    /// <remarks>
    /// Thirty kilometres. Without a limit the nearest place is always found and
    /// always used, so a photograph taken at sea or in a national park is
    /// labelled with a town an hour's drive away and nothing on screen says how
    /// far. Measured against this library's own case: the coordinates for
    /// Genting Highlands land 9 km from Kampung Bukit Tinggi, which is a fair
    /// description of where the photograph was taken; a hundred kilometres would
    /// not be.
    /// </remarks>
    public const double MaxKilometres = 30d;

    /// <summary>Kilometres in one degree of latitude, which does not vary.</summary>

    private const string ResourceName = "PhotoGallery.Infrastructure.Places.cities500.br";

    private readonly Func<Stream?> _open;
    private readonly Lock _loading = new();

    private Places? _places;

    public GeoNamesGazetteer()
        : this(OpenEmbedded)
    {
    }

    /// <summary>
    /// For tests, which supply a handful of rows as plain text.
    /// </summary>
    /// <remarks>
    /// Internal rather than public for the reason the tokenizer's constructor
    /// is: what is worth testing here is the search, and nothing outside this
    /// assembly should be choosing where the gazetteer comes from.
    /// </remarks>
    internal GeoNamesGazetteer(Func<Stream?> open) => _open = open;

    /// <summary>The compiled-in data, decompressed on the way out.</summary>
    private static Stream? OpenEmbedded()
    {
        Stream? compressed = typeof(GeoNamesGazetteer)
            .GetTypeInfo()
            .Assembly
            .GetManifestResourceStream(ResourceName);

        return compressed is null
            ? null
            : new BrotliStream(compressed, CompressionMode.Decompress);
    }

    public GazetteerPlace? Resolve(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsNaN(longitude)
            || Math.Abs(latitude) > 90d || Math.Abs(longitude) > 180d)
        {
            return null;
        }

        if (Loaded() is not Places places)
        {
            return null;
        }

        // A degree of longitude is only 111 km at the equator, so the number of
        // columns to sweep depends on where we are. Latitude never varies, so
        // one row either side always covers the radius.
        double kilometresPerLongitudeDegree =
            Coordinates.KilometresPerDegree * Math.Cos(latitude * Math.PI / 180d);

        int columns = kilometresPerLongitudeDegree < 0.001d
            ? 180
            : (int)Math.Ceiling(MaxKilometres / kilometresPerLongitudeDegree);

        int latitudeCell = (int)Math.Floor(latitude);
        int longitudeCell = (int)Math.Floor(longitude);

        int best = -1;
        double bestKilometres = double.MaxValue;

        for (int row = -1; row <= 1; row++)
        {
            for (int column = -columns; column <= columns; column++)
            {
                if (!places.Cells.TryGetValue(
                        Key(latitudeCell + row, longitudeCell + column),
                        out List<int>? candidates))
                {
                    continue;
                }

                foreach (int at in candidates)
                {
                    double kilometres = Coordinates.Kilometres(
                        latitude, longitude, places.Latitudes[at], places.Longitudes[at]);

                    if (kilometres < bestKilometres)
                    {
                        bestKilometres = kilometres;
                        best = at;
                    }
                }
            }
        }

        if (best < 0 || bestKilometres > MaxKilometres)
        {
            return null;
        }

        return new GazetteerPlace(
            places.Ids[best],
            places.Names[best],
            places.Countries[best],
            places.Admin1[best],
            places.Latitudes[best],
            places.Longitudes[best],
            bestKilometres);
    }

    /// <summary>One cell of the grid, as a single comparable number.</summary>
    private static int Key(int latitudeCell, int longitudeCell)
    {
        // Wrapped, so a photograph beside the antimeridian still sees the cells
        // on the other side of it.
        int wrapped = ((longitudeCell + 180) % 360 + 360) % 360;

        return ((Math.Clamp(latitudeCell, -90, 90) + 90) * 360) + wrapped;
    }

    private Places? Loaded()
    {
        if (_places is Places already)
        {
            return already;
        }

        lock (_loading)
        {
            if (_places is null && Read() is Places read)
            {
                _places = read;
            }

            return _places;
        }
    }

    /// <summary>
    /// Reads the data into parallel arrays and buckets it.
    /// </summary>
    /// <remarks>
    /// Arrays rather than a list of objects: 235,403 small records would be as
    /// many allocations and a great deal more memory than six flat arrays, and
    /// the lookup only ever touches two of them until it has a winner.
    ///
    /// <para>Six tab-separated columns - id, name, latitude, longitude, country,
    /// first-level division - which is GeoNames' layout with the thirteen this
    /// app never reads already removed. The id is GeoNames' own and is kept
    /// because it is stable across their releases: a row number would shift
    /// under a later dump and leave stored places pointing at other towns.</para>
    ///
    /// <para>A row that does not parse is skipped rather than failing the load.
    /// One malformed line in a third of a million must not cost the whole
    /// feature.</para>
    /// </remarks>
    private Places? Read()
    {
        try
        {
            var ids = new List<int>(250_000);
            var names = new List<string>(250_000);
            var countries = new List<string?>(250_000);
            var admin1 = new List<string?>(250_000);
            var latitudes = new List<double>(250_000);
            var longitudes = new List<double>(250_000);
            var cells = new Dictionary<int, List<int>>(40_000);

            using Stream? source = _open();
            if (source is null)
            {
                return null;
            }

            using var reader = new StreamReader(source, System.Text.Encoding.UTF8);

            while (reader.ReadLine() is string line)
            {
                string[] fields = line.Split('\t');

                if (fields.Length < 4
                    || !int.TryParse(fields[0], NumberStyles.Integer,
                           CultureInfo.InvariantCulture, out int id)
                    || !double.TryParse(fields[2], NumberStyles.Float,
                           CultureInfo.InvariantCulture, out double latitude)
                    || !double.TryParse(fields[3], NumberStyles.Float,
                           CultureInfo.InvariantCulture, out double longitude)
                    || fields[1].Length == 0)
                {
                    continue;
                }

                int at = ids.Count;
                ids.Add(id);
                names.Add(fields[1]);
                countries.Add(fields.Length > 4 ? Trimmed(fields[4]) : null);
                admin1.Add(fields.Length > 5 ? Trimmed(fields[5]) : null);
                latitudes.Add(latitude);
                longitudes.Add(longitude);

                int key = Key((int)Math.Floor(latitude), (int)Math.Floor(longitude));
                if (!cells.TryGetValue(key, out List<int>? cell))
                {
                    cell = [];
                    cells[key] = cell;
                }

                cell.Add(at);
            }

            return ids.Count == 0
                ? null
                : new Places(
                    [.. ids], [.. names], [.. countries], [.. admin1],
                    [.. latitudes], [.. longitudes], cells);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or OutOfMemoryException)
        {
            // The feature goes quiet rather than the app falling over. A library
            // with no place names is the state it was in yesterday.
            return null;
        }
    }

    private static string? Trimmed(string field) =>
        field.Length == 0 ? null : field;

    /// <summary>The whole gazetteer, flattened, plus the grid that indexes it.</summary>
    private sealed record Places(
        int[] Ids,
        string[] Names,
        string?[] Countries,
        string?[] Admin1,
        double[] Latitudes,
        double[] Longitudes,
        Dictionary<int, List<int>> Cells);
}

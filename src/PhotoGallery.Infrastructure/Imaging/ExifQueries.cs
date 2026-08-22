namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Where the EXIF tags this app reads actually sit, per container.
/// </summary>
/// <remarks>
/// The same tag lives at a different metadata path depending on what wraps it: a
/// JPEG keeps EXIF inside its APP1 block, a HEIF keeps it at the top. Getting
/// this wrong does not throw - the query simply returns nothing, and the app
/// concludes the file carries no date and no rotation. It cost this library all
/// 864 of its HEIC photographs both, silently, until someone noticed the dates
/// were wrong.
///
/// <para>One list per tag, in one place, because that is the shape of the
/// mistake: reading in one class and writing in another, each knowing about half
/// the containers, is how a file gets read from a path it is never written
/// back to.</para>
/// </remarks>
internal static class ExifQueries
{
    /// <summary>DateTimeOriginal (36867), falling back to DateTime (306).</summary>
    public static readonly string[] DateTaken =
    [
        "/app1/ifd/exif/{ushort=36867}",  // JPEG, DateTimeOriginal
        "/ifd/exif/{ushort=36867}",       // HEIF, the same tag
        "/ifd/{ushort=306}",              // DateTime, where nothing else is offered
    ];

    /// <summary>Orientation (274), which says which way up the picture goes.</summary>
    public static readonly string[] Orientation =
    [
        "/app1/ifd/{ushort=274}",         // JPEG
        "/ifd/{ushort=274}",              // HEIF
    ];

    /// <summary>
    /// GPS latitude (2) and the hemisphere it is measured in (1).
    /// </summary>
    /// <remarks>
    /// The coordinate is meaningless without its reference: EXIF stores the
    /// magnitude only, so a photograph taken in Melbourne and one taken in
    /// Mongolia can carry the same latitude and differ solely by an "S" here.
    /// Reading one without the other silently puts half the southern hemisphere
    /// in the northern.
    /// </remarks>
    public static readonly string[] Latitude =
    [
        "/app1/ifd/gps/{ushort=2}",       // JPEG
        "/ifd/gps/{ushort=2}",            // HEIF
    ];

    public static readonly string[] LatitudeRef =
    [
        "/app1/ifd/gps/{ushort=1}",
        "/ifd/gps/{ushort=1}",
    ];

    /// <summary>GPS longitude (4) and its reference (3).</summary>
    public static readonly string[] Longitude =
    [
        "/app1/ifd/gps/{ushort=4}",
        "/ifd/gps/{ushort=4}",
    ];

    public static readonly string[] LongitudeRef =
    [
        "/app1/ifd/gps/{ushort=3}",
        "/ifd/gps/{ushort=3}",
    ];
}

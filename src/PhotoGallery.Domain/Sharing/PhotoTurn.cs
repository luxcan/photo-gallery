namespace PhotoGallery.Domain.Sharing;

/// <summary>Somebody saying which way up a photograph goes.</summary>
/// <remarks>
/// <paramref name="Rotation"/> is the app's own turn, for a file that would not
/// take the answer. Where the EXIF tag could be written, the turn travels with
/// rotation zero: the file holds the answer, and what travels is the moment,
/// which tells every other machine to check its cached pictures against the
/// file.
///
/// <para>Merged before face answers, which is what lets a face be keyed on its
/// box alone.</para>
/// </remarks>
/// <param name="Rotation">A quarter turn clockwise: 0, 90, 180 or 270.</param>
public sealed record PhotoTurn(
    AssetKey Photo,
    int Rotation,
    DateTime DecidedUtc,
    Guid DecidedBy);

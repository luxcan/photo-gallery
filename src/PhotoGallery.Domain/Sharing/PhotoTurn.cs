namespace PhotoGallery.Domain.Sharing;

/// <summary>Somebody saying which way up a photograph goes.</summary>
/// <remarks>
/// Carried because the file would not take the answer. Where the EXIF tag can be
/// written the turn reaches the other machines through the file itself and needs
/// no sharing at all; this is for the pictures whose format has nowhere to put
/// one.
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

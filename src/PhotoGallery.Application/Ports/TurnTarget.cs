using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.Ports;

/// <summary>The row a turn from another machine would land on.</summary>
/// <remarks>
/// A turn is the one merged decision that is not only a database write: the
/// cached pictures have to move and the boxes drawn on them have to move with
/// them. So applying one needs the rendition's name and the turn already
/// recorded here, which a key alone cannot give.
/// </remarks>
/// <param name="ThumbnailName">
/// Null when this photograph has not been prepared yet. There is nothing to turn
/// and the answer waits, rather than being recorded against a picture that will
/// be generated the right way up later and then turned twice.
/// </param>
public sealed record TurnTarget(
    AssetKey Photo,
    int AssetId,
    string? ThumbnailName,
    int Rotation);

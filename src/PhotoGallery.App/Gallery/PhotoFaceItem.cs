using PhotoGallery.App.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.App.Gallery;

/// <summary>
/// One face box drawn over the open photograph.
/// </summary>
/// <remarks>
/// Carries where to draw as well as what was found, because the detector works
/// in the cached preview's pixels and the picture on screen is whatever size the
/// window allows. Working that out once per face when the layout changes is
/// simpler to follow than a converter doing it per binding.
/// </remarks>
public sealed class PhotoFaceItem : PlacedFace
{
    public PhotoFaceItem(FaceOnPhoto face)
    {
        ArgumentNullException.ThrowIfNull(face);
        Face = face;
    }

    public FaceOnPhoto Face { get; }

    public int FaceId => Face.FaceId;

    protected override FaceBounds Bounds => Face.Bounds;

    public bool IsNamed => Face.IsNamed;

    public bool IsIgnored => Face.IsIgnored;

    /// <summary>What the box says under it.</summary>
    /// <remarks>
    /// A proposal is marked as a guess rather than shown as a fact, because the
    /// whole point of showing it is to be told whether it is right.
    /// </remarks>
    public string Label => Face.IsIgnored
        ? "Not tracked"
        : Face.PersonName is null
            ? "Who is this?"
            : Face.IsProposed ? $"{Face.PersonName}?" : Face.PersonName;
}

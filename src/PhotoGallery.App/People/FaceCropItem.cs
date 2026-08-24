using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoGallery.App.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.App.People;

/// <summary>
/// One face crop on screen, and where it sits on the picture it came out of.
/// </summary>
/// <remarks>
/// Both, because a crop alone is often not enough to answer the question being
/// asked of it. The same face is shown twice: small in the grid, where a
/// screenful can be judged at once, and outlined on the whole photograph when
/// the small one leaves any doubt.
/// </remarks>
public sealed partial class FaceCropItem : PlacedFace
{
    [ObservableProperty]
    private ImageSource? _picture;

    [ObservableProperty]
    private bool _isChosen = true;

    public FaceCropItem(FaceThumbnail face)
    {
        ArgumentNullException.ThrowIfNull(face);
        Face = face;
    }

    public FaceThumbnail Face { get; }

    public int FaceId => Face.FaceId;

    protected override FaceBounds Bounds => Face.Bounds;

    /// <summary>When the picture was taken, as far as anything knows.</summary>
    /// <remarks>
    /// Spelt out rather than given as a year, because it is the fact the app is
    /// learning from: an example from this date teaches what they looked like on
    /// it.
    /// </remarks>
    public string Caption => Face.TakenUtc.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>The file this face was found in.</summary>
    /// <remarks>
    /// Named the same way the photo viewer names it, so the picture can be
    /// recognised - or found outside this app - from either screen.
    /// </remarks>
    public string FileName => Path.GetFileName(Face.RelativePath);

    /// <summary>The folder it sits in, below the library root.</summary>
    public string FolderPath => FolderTree.FolderOf(Face.RelativePath);
}

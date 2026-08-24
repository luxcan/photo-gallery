using CommunityToolkit.Mvvm.ComponentModel;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.App.Imaging;

/// <summary>
/// A face that can be outlined over the picture it was found in.
/// </summary>
/// <remarks>
/// Two screens draw the same outline for different reasons - the viewer boxes
/// every face so one can be named, and the People screen boxes a single proposal
/// so it can be judged against the whole photograph - and both need the same four
/// numbers kept in step with the size the picture is drawn at.
/// </remarks>
public abstract partial class PlacedFace : ObservableObject
{
    [ObservableProperty]
    private double _left;

    [ObservableProperty]
    private double _top;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    /// <summary>Where the detector found this face, in the preview's pixels.</summary>
    protected abstract FaceBounds Bounds { get; }

    /// <summary>Places the box for a picture drawn to a given fit.</summary>
    public void PlaceWithin(PictureFit fit)
    {
        Left = (Bounds.X * fit.Scale) + fit.OffsetX;
        Top = (Bounds.Y * fit.Scale) + fit.OffsetY;
        Width = Bounds.Width * fit.Scale;
        Height = Bounds.Height * fit.Scale;
    }
}

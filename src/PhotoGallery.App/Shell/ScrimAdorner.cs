using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PhotoGallery.App.Shell;

/// <summary>
/// The shade a modal window is read against, drawn over the window it interrupts.
/// </summary>
/// <remarks>
/// The pass overlay is a panel inside the main window, so it dims what it covers
/// with a border in the markup above everything else. A dialog is a window of its
/// own and has nothing inside the window it interrupts to do that with, which is
/// why the app's message box used to float over a screen at full brightness and
/// read as a panel that had come loose rather than as something waiting for an
/// answer.
///
/// <para>An adorner rather than a second window laid over the first: it is
/// positioned and clipped by the element it adorns, so it cannot be left behind
/// by the window it belongs to and cannot outlive it. It costs the callers
/// nothing either - <see cref="AppDialog"/> raises and drops it around its own
/// <c>ShowDialog</c>, so none of the eleven places that ask a question had to
/// learn that a scrim exists.</para>
/// </remarks>
internal sealed class ScrimAdorner : Adorner
{
    /// <summary>
    /// The shade to paint, taken from the theme rather than written here, so it
    /// cannot drift from the one the pass overlay is read against.
    /// </summary>
    public static readonly DependencyProperty ShadeProperty =
        DependencyProperty.Register(
            nameof(Shade),
            typeof(Brush),
            typeof(ScrimAdorner),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private ScrimAdorner(UIElement adorned)
        : base(adorned)
    {
        // Modality has already disabled the window underneath, so there is
        // nothing here for the scrim to block; staying out of hit testing keeps
        // it from being what the cursor reports itself to be over.
        IsHitTestVisible = false;
        SetResourceReference(ShadeProperty, "ModalScrim");
    }

    public Brush? Shade
    {
        get => (Brush?)GetValue(ShadeProperty);
        set => SetValue(ShadeProperty, value);
    }

    /// <summary>
    /// Dims <paramref name="window"/> until the returned handle is disposed.
    /// </summary>
    /// <returns>
    /// Null when there is no window to dim - start-up, before there is one, and
    /// the one dialog that is shown then. Callers dispose it either way.
    /// </returns>
    public static IDisposable? Cover(Window? window)
    {
        if (window?.Content is not UIElement content)
        {
            return null;
        }

        // Present because a plain Window's template puts an AdornerDecorator
        // around its content. Both windows here use that template; a future one
        // that does not would go undimmed rather than throw.
        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(content);

        if (layer is null)
        {
            return null;
        }

        var scrim = new ScrimAdorner(content);
        layer.Add(scrim);

        return new Shading(layer, scrim);
    }

    /// <summary>
    /// Takes the size of what it covers, so that the rectangle drawn below is
    /// the whole window rather than the adorner's own idea of how big it is.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        return AdornedElement.RenderSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (Shade is not null)
        {
            drawingContext.DrawRectangle(Shade, null, new Rect(RenderSize));
        }
    }

    /// <summary>Takes the shade away again.</summary>
    private sealed class Shading : IDisposable
    {
        private readonly AdornerLayer _layer;
        private readonly Adorner _scrim;

        public Shading(AdornerLayer layer, Adorner scrim)
        {
            _layer = layer;
            _scrim = scrim;
        }

        public void Dispose() => _layer.Remove(_scrim);
    }
}

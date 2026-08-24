using System.Windows;
using System.Windows.Media;

namespace PhotoGallery.App.Theme;

/// <summary>
/// What a button looks like under the pointer, carried on the button itself.
/// </summary>
/// <remarks>
/// One template serves every button in the app, and a template trigger cannot
/// read a Style setter - so a trigger naming a brush directly fixes that state
/// for every style built on it. That is how the secondary button came to turn
/// accent-coloured on hover: it overrode its Background and inherited a hover
/// that had the primary button's colour written into it.
///
/// <para>Two attached properties instead, read by the trigger off the templated
/// parent, so each style says what its own states are and there is still one
/// template. The alternative - a second copy of the template per style - is how
/// a hover and a pressed state end up disagreeing about corner radius three
/// months later.</para>
/// </remarks>
public static class ButtonStates
{
    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "HoverBackground",
            typeof(Brush),
            typeof(ButtonStates),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "PressedBackground",
            typeof(Brush),
            typeof(ButtonStates),
            new FrameworkPropertyMetadata(null));

    /// <summary>
    /// The outline under the pointer. Here for the same reason as the two above:
    /// the outline is what tells a quiet button apart from the surface behind it,
    /// and the two styles want different ones.
    /// </summary>
    public static readonly DependencyProperty HoverBorderProperty =
        DependencyProperty.RegisterAttached(
            "HoverBorder",
            typeof(Brush),
            typeof(ButtonStates),
            new FrameworkPropertyMetadata(null));

    public static Brush? GetHoverBackground(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Brush?)element.GetValue(HoverBackgroundProperty);
    }

    public static void SetHoverBackground(DependencyObject element, Brush? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HoverBackgroundProperty, value);
    }

    public static Brush? GetPressedBackground(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Brush?)element.GetValue(PressedBackgroundProperty);
    }

    public static void SetPressedBackground(DependencyObject element, Brush? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PressedBackgroundProperty, value);
    }

    public static Brush? GetHoverBorder(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Brush?)element.GetValue(HoverBorderProperty);
    }

    public static void SetHoverBorder(DependencyObject element, Brush? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HoverBorderProperty, value);
    }
}

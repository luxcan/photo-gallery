using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoGallery.App.Albums;

/// <summary>
/// One thing with a tick against it: a person or a place in an album's rule, or
/// an album on a collection's shelf.
/// </summary>
/// <remarks>
/// Observable because the tick is the whole interaction. Each of these lists is
/// the thing being edited rather than a preview of one held somewhere else - the
/// rule is read off the people and places when Save is pressed, and the shelf is
/// read off the albums when Add is.
///
/// <para>It was RuleChoice while a rule was the only list of this shape. The
/// third use is what made the name wrong rather than merely narrow, and one type
/// taking an id, a name, a caption and a tick is the whole of what all three
/// need.</para>
/// </remarks>
public sealed partial class TickChoice : ObservableObject
{
    public TickChoice(int id, string name, string caption, bool isChosen)
    {
        Id = id;
        Name = name;
        Caption = caption;
        _isChosen = isChosen;
    }

    public int Id { get; }

    public string Name { get; }

    /// <summary>
    /// How many photographs they are in, were taken there, or the album holds.
    /// </summary>
    public string Caption { get; }

    [ObservableProperty]
    private bool _isChosen;
}

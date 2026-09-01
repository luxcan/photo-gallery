using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoGallery.App.Albums;

/// <summary>
/// One person or one place, on or off in an album's rule.
/// </summary>
/// <remarks>
/// Observable because the tick is the whole interaction: the rule is read off
/// these when it is saved, so the list is the editor rather than a preview of
/// one held somewhere else.
/// </remarks>
public sealed partial class RuleChoice : ObservableObject
{
    public RuleChoice(int id, string name, string caption, bool isChosen)
    {
        Id = id;
        Name = name;
        Caption = caption;
        _isChosen = isChosen;
    }

    public int Id { get; }

    public string Name { get; }

    /// <summary>How many photographs they are in, or were taken there.</summary>
    public string Caption { get; }

    [ObservableProperty]
    private bool _isChosen;
}

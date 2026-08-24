using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.People;

/// <summary>
/// One name offered for the face being pointed at.
/// </summary>
/// <remarks>
/// Carries whether it is the name already on that face, so the list can show
/// which one is current rather than making the user remember what they are
/// changing.
/// </remarks>
public sealed class PersonChoiceItem
{
    public PersonChoiceItem(PersonDirectoryEntry person, bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(person);
        Person = person;
        IsCurrent = isCurrent;
    }

    public PersonDirectoryEntry Person { get; }

    public bool IsCurrent { get; }

    public int Id => Person.Id;

    public string DisplayName => Person.DisplayName;
}

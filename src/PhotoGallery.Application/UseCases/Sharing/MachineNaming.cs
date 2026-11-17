namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>How giving another computer a name here went.</summary>
public enum MachineNaming
{
    /// <summary>The name was kept, or taken away where the box was cleared.</summary>
    Named,

    /// <summary>
    /// Another computer already goes by that name here, so nothing was kept.
    /// </summary>
    NameTaken,
}

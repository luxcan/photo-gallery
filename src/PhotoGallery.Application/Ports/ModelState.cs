namespace PhotoGallery.Application.Ports;

/// <summary>Whether a model file can be used.</summary>
public enum ModelState
{
    /// <summary>Nothing of that name is in the working folder.</summary>
    Missing = 0,

    /// <summary>Present, the right size, and the digest matches. Safe to open.</summary>
    Ready = 1,

    /// <summary>
    /// Something was there but it is not what the manifest describes, so it has
    /// been removed rather than half-used.
    /// </summary>
    Damaged = 2,
}

namespace PhotoGallery.Domain.Faces;

/// <summary>Whether a face's eyes are open, as far as can honestly be told.</summary>
public enum EyeState
{
    /// <summary>
    /// Cannot be told: turned away, one eye hidden, the landmarks unsure, or the
    /// eyes somewhere between open and closed. Never counted either way.
    /// </summary>
    Unknown = 0,

    /// <summary>Both eyes plainly open.</summary>
    Open = 1,

    /// <summary>Both eyes plainly shut.</summary>
    Closed = 2,
}

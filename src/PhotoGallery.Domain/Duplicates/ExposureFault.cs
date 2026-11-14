namespace PhotoGallery.Domain.Duplicates;

/// <summary>Why a copy lost on exposure, so its card can say which way it is wrong.</summary>
public enum ExposureFault
{
    /// <summary>Nothing wrong, or exposure did not decide anything about this copy.</summary>
    None = 0,

    /// <summary>Clearly more of it is blown white than the best copy.</summary>
    TooBright = 1,

    /// <summary>Clearly more of it is crushed black than the best copy.</summary>
    TooDark = 2,
}

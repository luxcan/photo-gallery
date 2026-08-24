namespace PhotoGallery.Domain.Duplicates;

/// <summary>
/// How certain a duplicate match is. The two are never merged into one list,
/// because they warrant different handling.
/// </summary>
public enum DuplicateKind
{
    /// <summary>Byte-identical. Provably the same file, safe to approve in bulk.</summary>
    Exact = 0,

    /// <summary>
    /// Visually near-identical by perceptual hash. Needs a human eye, because a
    /// perceptual hash cannot distinguish a re-saved copy from the next frame of
    /// a burst.
    /// </summary>
    Near = 1,
}

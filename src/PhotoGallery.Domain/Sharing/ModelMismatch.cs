namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// A machine whose vectors cannot be used, and which of its models differs.
/// </summary>
/// <remarks>
/// Named rather than counted. "Some vectors were refused" is a message nobody
/// can act on; "Dad's laptop is running a different face recognition model" is
/// one somebody can go and fix in ten minutes, and the fix - the same file in
/// both models folders - is the difference between two hours of face detection
/// and none.
/// </remarks>
public sealed record ModelMismatch(MachineIdentity Machine, IReadOnlyList<string> Models)
{
    /// <summary>What to put on screen, naming the machine and the model.</summary>
    public string Explain() =>
        Models.Count == 1
            ? $"{Machine.Name} is running a different {Models[0]} model, so the faces it has "
            + "already found were not taken. Its answers and pictures were."
            : $"{Machine.Name} is running different {string.Join(" and ", Models)} models, so "
            + "the faces it has already found were not taken. Its answers and pictures were.";
}

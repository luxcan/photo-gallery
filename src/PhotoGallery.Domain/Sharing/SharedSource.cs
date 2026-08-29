namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// One folder of photographs, as a machine names it to the others.
/// </summary>
/// <remarks>
/// The identity is a <see cref="Guid"/> and never the root, because a root is
/// machine-local text: <c>\\192.168.50.103\PhotoGallery</c> on one laptop is
/// <c>Z:\</c> on the next, and Windows offers to map that drive letter for you.
/// Putting the root in a key would rebuild the drive-letter problem one layer
/// down.
///
/// <para>The root travels anyway, and only so that two folders can be
/// <em>proposed</em> as the same one. Nothing is matched on it and nothing is
/// decided by it - a person confirms the pair, once, and from then on the two
/// sources share an id.</para>
/// </remarks>
/// <param name="Root">
/// What this machine calls the folder. Shown to a person deciding whether two
/// roots are the same place, and used for nothing else.
/// </param>
/// <param name="Photographs">
/// How many files this machine has indexed under it. A second signal for the
/// same question: two roots holding sixteen thousand files each are a likelier
/// pair than one holding sixteen thousand and one holding nine.
/// </param>
public sealed record SharedSource(Guid SharedId, string Root, int Photographs)
{
    /// <summary>
    /// The last part of the root, which is the part worth comparing.
    /// </summary>
    /// <remarks>
    /// <c>\\192.168.50.103\PhotoGallery</c> and <c>Z:\PhotoGallery</c> agree
    /// here and nowhere else. A drive mapped to the share itself - <c>Z:\</c> -
    /// agrees nowhere at all, which is why this proposes rather than decides.
    /// </remarks>
    public string Leaf =>
        Root.TrimEnd('\\', '/').Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            is [.., string last]
            ? last
            : Root.Trim();
}

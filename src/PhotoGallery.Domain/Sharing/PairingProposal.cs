namespace PhotoGallery.Domain.Sharing;

/// <summary>Why two folders look like they might be the same one.</summary>
public enum PairingLikeness
{
    /// <summary>The two roots are the same text. As sure as this gets.</summary>
    SamePath = 0,

    /// <summary>
    /// The roots end in the same folder name, reached by different routes.
    /// </summary>
    /// <remarks>
    /// <c>\\192.168.50.103\PhotoGallery</c> and <c>Z:\PhotoGallery</c>. The
    /// ordinary case, and the one worth proposing.
    /// </remarks>
    SameName = 1,

    /// <summary>
    /// One root sits inside the other, so the paths below them differ by a
    /// prefix.
    /// </summary>
    /// <remarks>
    /// <strong>Not a pair, and the reason this is reported rather than hidden.
    /// </strong> <c>\\...\PhotoGallery</c> against <c>\\...\PhotoGallery\Photos</c>
    /// is two machines filing the same pictures at different depths: every key
    /// misses, every merge matches nothing, and the exchange looks merely empty.
    /// Pairing them would not help - the paths below would still differ - so the
    /// answer is to say what is wrong, and let somebody move a source.
    /// </remarks>
    FiledDifferently = 2,
}

/// <summary>
/// Two folders that might be the same one, put to a person.
/// </summary>
/// <remarks>
/// Proposed and never assumed. Matching roots is not string equality and must
/// not be built as though it were, but nor can it be guessed: absorbing two
/// unrelated folders into one id would put every photograph in one under a key
/// that means a different photograph in the other, and nothing would ever say
/// so. So the app finds the likely pairs and a person confirms, once.
/// </remarks>
public sealed record PairingProposal(
    SharedSource Mine,
    SharedSource Theirs,
    string MachineName,
    PairingLikeness Likeness)
{
    /// <summary>Whether confirming this is a thing the user can do.</summary>
    /// <remarks>
    /// False for <see cref="PairingLikeness.FiledDifferently"/>, which is a
    /// diagnosis rather than an offer: linking those two ids would leave every
    /// path below them still differing by a prefix.
    /// </remarks>
    public bool CanPair => Likeness != PairingLikeness.FiledDifferently;
}

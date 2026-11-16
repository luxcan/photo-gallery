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
    /// <c>\\nas\Photos</c> and <c>Z:\Photos</c>. The
    /// ordinary case, and the one worth proposing.
    /// </remarks>
    SameName = 1,

    /// <summary>
    /// One root sits inside the other, so the paths below them differ by a
    /// prefix.
    /// </summary>
    /// <remarks>
    /// <strong>Not a pair, and the reason this is reported rather than hidden.
    /// </strong> <c>\\...\Photos</c> against <c>\\...\Photos\Family</c>
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

    /// <summary>
    /// The computer that keeps <see cref="Theirs"/>, where the asker needs to
    /// know which one; empty otherwise.
    /// </summary>
    /// <remarks>
    /// Carried for the question adding a folder asks, whose yes also means
    /// "take this computer's pictures and faces" - and that has to be recorded
    /// against the computer, not its name, which any file can claim.
    /// </remarks>
    public Guid MachineId { get; init; }

    /// <summary>
    /// Whether another computer goes by <see cref="MachineName"/> too, where
    /// the asker needs to know; false otherwise.
    /// </summary>
    /// <remarks>
    /// A copy of a laptop's own file, planted under a fresh id, proposes the
    /// same folder under the same name, and the two questions read alike. A yes
    /// to one of those is then a yes to a folder and never to a computer: which
    /// computer to take changes from is left to the Sync screen, which says
    /// that two are calling themselves the same thing.
    /// </remarks>
    public bool SharesItsName { get; init; }
}

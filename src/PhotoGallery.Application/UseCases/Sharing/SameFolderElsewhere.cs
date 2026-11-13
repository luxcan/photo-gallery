using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// What a folder just added has in common with the other computers.
/// </summary>
/// <param name="SyncFolderChosen">
/// Whether this library syncs through a folder at all. False is the new laptop
/// that has not been told where the others leave their work yet.
/// </param>
/// <param name="Proposals">
/// Folders on other computers that might be this one, likeliest first. Empty
/// when there is no sync folder, it does not answer, or nothing matches.
/// </param>
public sealed record SameFolderElsewhere(
    bool SyncFolderChosen, IReadOnlyList<PairingProposal> Proposals)
{
    /// <summary>No sync folder chosen, so nobody to ask about.</summary>
    public static SameFolderElsewhere NoSyncFolder { get; } = new(false, []);

    /// <summary>A sync folder chosen, and nothing in it to ask about.</summary>
    public static SameFolderElsewhere Nothing { get; } = new(true, []);
}

using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.App.ViewModels;

/// <summary>
/// The questions adding a photo folder may put to the user before its first
/// scan, answered by the window.
/// </summary>
/// <remarks>
/// Asked then because the first scan is the one that can take the pictures the
/// other computers have already made - but only through a sync folder, and only
/// for a folder the user has said is the same as theirs. Asked any later, that
/// scan has already spent hours making them itself.
/// </remarks>
public interface IAddFolderQuestions
{
    /// <summary>
    /// Offers to choose the sync folder before the first folder's first scan.
    /// </summary>
    /// <returns>The folder chosen, or null to carry on without one.</returns>
    string? ChooseSyncFolderFirst();

    /// <summary>Asks whether a folder here is the same as one on another computer.</summary>
    bool IsSameFolder(PairingProposal proposal);
}

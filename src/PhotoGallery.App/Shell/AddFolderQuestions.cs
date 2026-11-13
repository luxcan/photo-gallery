using System.Windows;
using Microsoft.Win32;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Puts the questions adding a photo folder asks to the user, over the window.
/// </summary>
public sealed class AddFolderQuestions : IAddFolderQuestions
{
    private readonly Window _owner;

    public AddFolderQuestions(Window owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public string? ChooseSyncFolderFirst()
    {
        bool choose = AppDialog.Confirm(
            _owner,
            "Do your other computers already use Photo Gallery?",
            "If they do, choose the sync folder they use. This computer then takes the "
            + "pictures they have already made instead of spending hours making its own.",
            confirm: "Choose sync folder...");

        if (!choose)
        {
            return null;
        }

        var dialog = new OpenFolderDialog { Title = "Choose a folder every computer can reach" };
        return dialog.ShowDialog(_owner) == true ? dialog.FolderName : null;
    }

    public bool IsSameFolder(PairingProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        return AppDialog.Confirm(
            _owner,
            "Is this the same folder?",
            $"{proposal.MachineName} keeps photos in {proposal.Theirs.Root}. Is that the "
            + $"same folder as {proposal.Mine.Root}?\n\nIf it is, this computer takes the "
            + "pictures and faces it has already found instead of finding them again.",
            confirm: "Same folder");
    }
}

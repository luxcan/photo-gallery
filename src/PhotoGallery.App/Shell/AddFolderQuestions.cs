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

        // Where two computers go by one name the yes cannot say which of them
        // it meant, so it is about the folder alone, and says so.
        string then = proposal.SharesItsName
            ? $"Another computer is also calling itself {proposal.MachineName}, so nothing "
              + "is taken from either until you say which one to take changes from on the "
              + "Sync screen."
            : $"If it is, this computer takes the pictures, faces and changes from "
              + $"{proposal.MachineName} from now on, instead of finding them again. Only say "
              + "yes to a computer you recognise.";

        return AppDialog.Confirm(
            _owner,
            "Is this the same folder?",
            $"{proposal.MachineName} keeps photos in {proposal.Theirs.Root}. Is that the "
            + $"same folder as {proposal.Mine.Root}?\n\n{then}",
            confirm: "Same folder");
    }
}

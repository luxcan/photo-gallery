using System.Windows;
using PhotoGallery.App.Sharing;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.App.Shell;

/// <summary>Asks what to call another computer, in the app's own dialog.</summary>
public sealed class MachineNameQuestion : IMachineNameQuestion
{
    private readonly Window _owner;

    public MachineNameQuestion(Window owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public string? AskName(string current, string ownName, string? problem) =>
        AppDialog.AskForText(
            _owner,
            // Named, because "this computer" is the one the app is running on
            // everywhere else it says so.
            $"Rename {current}",
            current,
            emptyHint: ownName,
            confirm: "Rename",
            maxLength: KnownMachine.NameLength,
            problem);
}

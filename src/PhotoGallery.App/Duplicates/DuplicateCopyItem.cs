using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Duplicates;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.App.Duplicates;

/// <summary>One copy of a duplicated picture, as the review screen draws it.</summary>
public sealed partial class DuplicateCopyItem : ObservableObject
{
    [ObservableProperty]
    private ImageSource? _picture;

    /// <summary>
    /// Whether this copy survives. Ticked means kept; whatever is left unticked
    /// in a group is what Delete removes.
    /// </summary>
    /// <remarks>
    /// One at a time: ticking a copy unticks the rest of its group. That is
    /// enforced by the group rather than by making these radio buttons, because
    /// a radio cannot be un-pressed - and unticking the last one is what lifts a
    /// group back out of the decision.
    ///
    /// <para>Nothing starts ticked. An untouched group is not part of what any
    /// Delete button does, which is what makes a button acting on the whole
    /// screen safe.</para>
    /// </remarks>
    [ObservableProperty]
    private bool _isKept;

    public DuplicateCopyItem(DuplicateCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        Copy = copy;
        Details = PhotoDetails.Of(copy, FileName, FolderPath);
    }

    public DuplicateCopy Copy { get; }

    /// <summary>
    /// Everything known about this copy, in the panel every screen shares.
    /// </summary>
    /// <remarks>
    /// Built once here rather than in the view model, because the card under the
    /// thumbnail wants the same facts the detail panel does - and formatting a
    /// date twice is how two parts of one screen end up disagreeing about it.
    /// </remarks>
    public PhotoDetails Details { get; }

    public int AssetId => Copy.AssetId;

    public bool IsKeeper => Copy.Role == DuplicateRole.Keeper;

    public string FileName => Path.GetFileName(Copy.RelativePath);

    public string FolderPath => FolderTree.FolderOf(Copy.RelativePath);

    /// <summary>The size in round terms, which is all a thumbnail card has room for.</summary>
    public string SizeCaption => DuplicateScan.Gigabytes(Copy.Length);

    /// <summary>
    /// What the app makes of this copy, said plainly rather than in a distance.
    /// </summary>
    /// <remarks>
    /// A suggestion and not a decision, because nothing is chosen until the user
    /// chooses it. A number of differing bits means nothing to the person
    /// looking at two photographs; what helps is knowing which one the app would
    /// have picked and how alike it thinks they are.
    /// </remarks>
    public string RoleCaption => IsKeeper
        ? "Suggested"
        : Copy.Distance == 0
            ? string.Empty
            : $"{Copy.Distance} of 64 bits differ";
}

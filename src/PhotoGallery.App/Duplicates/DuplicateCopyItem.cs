using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Duplicates;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Faces;

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
    /// chooses it. The suggested copy says which rung of the ladder chose it,
    /// and a copy beaten says what was wrong with it, so the reason can be
    /// checked against the pictures (PRP 14). A copy that only lost on size
    /// says nothing extra: its size is already on the card.
    /// </remarks>
    public string RoleCaption => IsKeeper
        ? SuggestedCaption(Copy.DecidedBy)
        : LostCaption() ?? DistanceCaption;

    /// <summary>
    /// The known faces whose closed eyes cost this copy, as boxes over the
    /// picture in the preview's pixels.
    /// </summary>
    public IReadOnlyList<FaceBounds> ClosedMarkers =>
        Copy.PreviewWidth > 0 && Copy.PreviewHeight > 0
            ? [.. (Copy.ClosedFaces ?? []).Select(face => face.Bounds)]
            : [];

    public bool HasClosedMarkers => ClosedMarkers.Count > 0;

    /// <summary>The size of the space the markers are placed in, so they scale with the tile.</summary>
    public int MarkerSpaceWidth => Copy.PreviewWidth;

    public int MarkerSpaceHeight => Copy.PreviewHeight;

    private string DistanceCaption => Copy.Distance == 0
        ? string.Empty
        : $"{Copy.Distance} of 64 bits differ";

    private static string SuggestedCaption(KeeperCriterion? decidedBy) => decidedBy switch
    {
        KeeperCriterion.Eyes => "Suggested: eyes open",
        KeeperCriterion.Sharpness => "Suggested: sharpest",
        KeeperCriterion.Exposure => "Suggested: best exposed",
        KeeperCriterion.Original => "Suggested: original file",
        KeeperCriterion.Pixels => "Suggested: largest picture",
        _ => "Suggested",
    };

    private string? LostCaption() => Copy.DecidedBy switch
    {
        KeeperCriterion.Eyes => ClosedCaption(Copy.ClosedNames),
        KeeperCriterion.Sharpness => "Blurred",
        KeeperCriterion.Exposure => Copy.Exposure switch
        {
            ExposureFault.TooBright => "Too bright",
            ExposureFault.TooDark => "Too dark",
            _ => null,
        },
        KeeperCriterion.Original => "Re-saved copy",
        _ => null,
    };

    /// <summary>
    /// Names the person whose eyes are closed, or counts them when there are
    /// several - a card has room for one name, not a list.
    /// </summary>
    private static string? ClosedCaption(IReadOnlyList<string> names) => names.Count switch
    {
        0 => null,
        1 => $"{names[0]}'s eyes are closed",
        _ => $"{names.Count} people's eyes are closed",
    };
}

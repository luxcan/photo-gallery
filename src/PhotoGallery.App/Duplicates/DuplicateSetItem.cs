using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Duplicates;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.App.Duplicates;

/// <summary>One group of copies of the same picture, and which of them stay.</summary>
public sealed partial class DuplicateSetItem : ObservableObject
{
    public DuplicateSetItem(DuplicateSetView set)
    {
        ArgumentNullException.ThrowIfNull(set);

        Set = set;

        foreach (DuplicateCopy copy in set.Copies)
        {
            var item = new DuplicateCopyItem(copy);
            item.PropertyChanged += OnCopyChanged;
            Copies.Add(item);
        }
    }

    public DuplicateSetView Set { get; }

    public ObservableCollection<DuplicateCopyItem> Copies { get; } = [];

    public int Id => Set.Id;

    /// <summary>
    /// Whether more than one copy in this group may be kept.
    /// </summary>
    /// <remarks>
    /// Never for identical copies: the bytes are the same, so a second one is
    /// storage spent on nothing. Always for the visually alike, where the app is
    /// guessing - a burst of shots seconds apart lands in one group and several
    /// of them can be photographs worth having.
    /// </remarks>
    public bool AllowsMany => Set.Kind == DuplicateKind.Near;

    public IReadOnlyList<DuplicateCopyItem> Kept => [.. Copies.Where(copy => copy.IsKept)];

    public IReadOnlyList<DuplicateCopyItem> Doomed => [.. Copies.Where(copy => !copy.IsKept)];

    /// <summary>
    /// Whether a decision has been made about this group.
    /// </summary>
    /// <remarks>
    /// Nothing is chosen until the user chooses it, so an untouched group is
    /// simply not part of what any Delete button does. That is what makes a
    /// button acting on the whole screen safe: it can only ever reach groups
    /// somebody has looked at.
    /// </remarks>
    public bool CanDelete => Kept.Count > 0 && Doomed.Count > 0;

    /// <summary>What deleting this group would reclaim.</summary>
    public long DoomedBytes => Bytes(Doomed);

    public string Caption => Copies.Count == 2
        ? "2 copies of this picture"
        : $"{Copies.Count} copies of this picture";

    /// <summary>
    /// What the button would do, said as the whole sentence and counted rather
    /// than implied, because it cannot be undone.
    /// </summary>
    /// <remarks>
    /// Both halves are named - what stays and what goes - so the button can
    /// never be pressed on the belief that it does the opposite.
    /// </remarks>
    public string DeleteCaption
    {
        get
        {
            int kept = Kept.Count;
            IReadOnlyList<DuplicateCopyItem> doomed = Doomed;

            if (kept == 0)
            {
                return AllowsMany
                    ? "Tick the copies you want to keep"
                    : "Tick the copy you want to keep";
            }

            if (doomed.Count == 0)
            {
                return "Keeping every copy";
            }

            string keeping = kept == 1 ? "Keep this one" : $"Keep these {kept}";
            string losing = doomed.Count == 1
                ? "delete the other"
                : $"delete the other {doomed.Count}";

            return $"{keeping} and {losing} ({DuplicateScan.Gigabytes(Bytes(doomed))})";
        }
    }

    private static long Bytes(IReadOnlyList<DuplicateCopyItem> copies) =>
        copies.Sum(copy => copy.Copy.Length);

    /// <summary>
    /// Guards the un-ticking below from being taken for a fresh choice and
    /// starting the whole thing again.
    /// </summary>
    private bool _settling;

    /// <summary>
    /// Keeps a group down to one kept copy, and keeps the totals honest.
    /// </summary>
    /// <remarks>
    /// One copy of a photograph is what anybody wants, so ticking one unticks
    /// the rest. Enforced here rather than by making these radio buttons,
    /// because a radio cannot be un-pressed - and being able to untick the last
    /// one is what lifts a group back out of the decision, which is what makes
    /// the button acting on the whole screen safe.
    /// </remarks>
    private void OnCopyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DuplicateCopyItem.IsKept))
        {
            return;
        }

        if (!AllowsMany && !_settling && sender is DuplicateCopyItem chosen && chosen.IsKept)
        {
            _settling = true;
            try
            {
                foreach (DuplicateCopyItem other in Copies)
                {
                    if (!ReferenceEquals(other, chosen))
                    {
                        other.IsKept = false;
                    }
                }
            }
            finally
            {
                _settling = false;
            }
        }

        OnPropertyChanged(nameof(Kept));
        OnPropertyChanged(nameof(Doomed));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(DeleteCaption));
    }
}

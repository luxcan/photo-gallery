using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoGallery.App.ViewModels;

/// <summary>One photo source as a row in the sources table.</summary>
public sealed partial class PhotoSourceItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhotoCountDisplay))]
    private int _photoCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhotoCountDisplay), nameof(LastScanDisplay))]
    private DateTime? _lastScanUtc;

    /// <summary>True while this source is being scanned, so its row can say so.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLastScan), nameof(ShowsUnavailable))]
    private bool _isScanning;

    /// <summary>
    /// True when the last crawl could not reach the folder at all - an unplugged
    /// drive, a share that is not mounted.
    /// </summary>
    /// <remarks>
    /// Worth its own state rather than a stale date, because nothing about the
    /// source was established: its pictures are still indexed and still counted,
    /// and the row must not imply they were checked.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLastScan), nameof(ShowsUnavailable))]
    private bool _isUnavailable;

    public PhotoSourceItem(int id, string path, int photoCount, DateTime? lastScanUtc)
    {
        Id = id;
        Path = path;
        _photoCount = photoCount;
        _lastScanUtc = lastScanUtc;
    }

    public int Id { get; }

    public string Path { get; }

    /// <summary>The last path segment, useful where the full path will not fit.</summary>
    public string Name
    {
        get
        {
            string trimmed = Path.TrimEnd('\\', '/');
            int cut = trimmed.LastIndexOfAny(['\\', '/']);
            return cut < 0 ? trimmed : trimmed[(cut + 1)..];
        }
    }

    // An em dash rather than 0: nothing has been counted yet, which is not the
    // same as having counted zero photos.
    public string PhotoCountDisplay => LastScanUtc is null
        ? "—"
        : PhotoCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Whether the row's "Last scanned" cell shows the date.</summary>
    /// <remarks>
    /// The cell says one thing at a time - the date, "Scanning...", or "Not
    /// reachable" - and a scan running now outranks what the last one found.
    /// Stacked in one cell, a TextBlock has no background to hide the one
    /// beneath it, so each needs its own answer rather than a place on top.
    /// </remarks>
    public bool ShowsLastScan => !IsScanning && !IsUnavailable;

    /// <summary>Whether the row's "Last scanned" cell says "Not reachable".</summary>
    public bool ShowsUnavailable => IsUnavailable && !IsScanning;

    public string LastScanDisplay => LastScanUtc is null
        ? "Never"
        : LastScanUtc.Value.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);
}

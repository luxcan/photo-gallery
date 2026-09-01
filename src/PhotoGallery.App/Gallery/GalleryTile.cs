using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.App.Gallery;

/// <summary>One cell of the grid.</summary>
public sealed partial class GalleryTile : ObservableObject
{
    /// <summary>
    /// Filled once the tile has been decoded off the UI thread. Null means there
    /// is nothing to draw yet - either the picture has not been prepared, or its
    /// rendition has gone - and the cell shows a placeholder of the same size so
    /// the grid does not shift when it arrives.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture))]
    private ImageSource? _picture;

    public GalleryTile(GalleryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        ThumbnailName = item.ThumbnailName;
    }

    /// <summary>
    /// Whether this one is switched on, where the grid is asking a question.
    /// </summary>
    /// <remarks>
    /// Only the album suggestions use it, and they start every tile on -
    /// the same bargain the face review makes, so a screenful is accepted with
    /// one press and the odd wrong one is switched off first. Everywhere else
    /// the grid shows rather than asks, and nothing reads this.
    /// </remarks>
    [ObservableProperty]
    private bool _isChosen;

    public GalleryItem Item { get; }

    /// <summary>
    /// Which rendition to draw, which is not fixed for the life of the tile.
    /// </summary>
    /// <remarks>
    /// A rendition is named after the picture's content, so preparing a picture
    /// gives it a new name. A grid loaded before the pass ran therefore holds
    /// names that are about to become wrong, and a tile that kept asking for its
    /// original name would stay blank forever while the file it needs sits on
    /// disk under another one.
    /// </remarks>
    public string? ThumbnailName { get; set; }

    /// <summary>
    /// Whether a rendition for this picture exists on disk.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="HasPicture"/>, which only says whether it is
    /// decoded at this moment. Since decoded pictures are released once they are
    /// far from the viewport, reading "not decoded" as "not prepared" would
    /// report almost the whole library as outstanding.
    /// </remarks>
    public bool IsPrepared { get; set; }

    public bool HasPicture => Picture is not null;

    public bool IsVideo => Item.Kind == AssetKind.Video;

    /// <summary>
    /// How long the clip runs, as a person would read it, or nothing when it is
    /// not known.
    /// </summary>
    /// <remarks>
    /// Hours only appear on a clip that has them, so a thirty-second video reads
    /// "0:30" rather than "0:00:30". Nothing is shown at all when the length was
    /// never learnt - the shell hands back a picture and does not say how long
    /// the film is - and the badge is then the glyph alone, which still
    /// distinguishes a video from a photograph.
    /// </remarks>
    public string DurationCaption => Item.Duration is not TimeSpan length
        ? string.Empty
        : length >= TimeSpan.FromHours(1)
            ? length.ToString(@"h\:mm\:ss")
            : length.ToString(@"m\:ss");

    public bool HasDuration => DurationCaption.Length > 0;

    public string FileName => Item.FileName;

    /// <summary>
    /// What the tile says on hover. The folder is named rather than the date,
    /// because in this library the folder carries the meaning and, unlike a file
    /// date, it is always true.
    /// </summary>
    public string Caption =>
        Item.FolderPath.Length == 0 ? Item.FileName : $"{Item.FolderPath}\\{Item.FileName}";
}

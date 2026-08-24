namespace PhotoGallery.Domain.Assets;

/// <summary>
/// One media file in the library.
/// </summary>
/// <remarks>
/// Paths are stored relative to their source's root rather than absolutely, so
/// the index survives a source moving or being remounted under a new address -
/// one edit to the source row instead of one per asset.
/// </remarks>
public sealed class Asset
{
    public int Id { get; set; }

    /// <summary>Which photo source this file was found in.</summary>
    public int PhotoSourceId { get; set; }

    /// <summary>Path below its source's root, e.g. <c>20230203 - Chingay\IMG_6769.MOV</c>.</summary>
    public required string RelativePath { get; set; }

    public long Length { get; set; }

    public DateTime ModifiedUtc { get; set; }

    /// <summary>
    /// The file's creation time on its storage.
    /// </summary>
    /// <remarks>
    /// Not the day the picture was taken. Windows preserves a file's modified
    /// time across a copy and resets its creation time to the moment of copying,
    /// so for an archive assembled by copying this records when it arrived here.
    /// Measured on this library: 3,000 photos spanning eight years carry 13
    /// distinct creation days, one per bulk copy.
    /// </remarks>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// When a scan first indexed this file.
    /// </summary>
    /// <remarks>
    /// The app's own record of when the picture joined the library, which is the
    /// only trustworthy answer to "what did I add recently?". The file system
    /// cannot answer it: measured over 3,000 photos on the real share, creation
    /// time is the moment of the copy, collapsing eight years onto thirteen days.
    ///
    /// <para>Set once, when the row is created. A file changing does not change
    /// when it arrived.</para>
    /// </remarks>
    public DateTime IndexedUtc { get; set; }

    public AssetKind Kind { get; set; }

    /// <summary>
    /// How far this file has got through preparation, and the only thing the
    /// generating pass selects on.
    /// </summary>
    public AssetStatus Status { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary>How long the clip runs, or null for a photograph.</summary>
    /// <remarks>
    /// Read from the container's own header when the video is opened for its
    /// keyframes, because that open is the only one this app ever makes: at
    /// 6.4 MB/s over the share, asking a second time what a file already told us
    /// costs more than storing the answer ever will.
    ///
    /// <para>Null for a video means the pass has not reached it, or the
    /// container would not say - not that the clip has no length.</para>
    /// </remarks>
    public TimeSpan? Duration { get; set; }

    /// <summary>When the photo was taken, from EXIF. Null when the file carries none.</summary>
    public DateTime? TakenUtc { get; set; }

    /// <summary>
    /// Where the photo was taken, from GPS EXIF. Null for cameras without GPS,
    /// which is most of them.
    /// </summary>
    /// <remarks>
    /// Measured rather than assumed, over two independent samples of 200
    /// photographs drawn at random from this library and read from the share:
    /// 16.0% and 18.5% carried coordinates. So roughly one photograph in six -
    /// about 1,900 of the 11,098 here.
    /// </remarks>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>
    /// The place those coordinates were resolved to, or null when they have not
    /// been resolved yet - or cannot be.
    /// </summary>
    /// <remarks>
    /// Separate from the coordinates and filled by a separate pass, because the
    /// two are known at different moments: coordinates come free with the read
    /// that prepares a picture, and naming them needs a gazetteer that may not
    /// be installed. A photograph can sit with coordinates and no place for as
    /// long as it likes.
    ///
    /// <para>Deliberately <em>not</em> written by the preparing pass. Re-preparing
    /// a photograph - a cleared cache, a new preview size - must not cost it its
    /// place, and the only thing that clears this is the file's bytes actually
    /// changing, where the coordinates themselves are no longer to be trusted
    /// either.</para>
    /// </remarks>
    public int? PlaceId { get; set; }

    /// <summary>
    /// When this photograph's location was last worked out, or null when it never
    /// has been.
    /// </summary>
    /// <remarks>
    /// The same device as <see cref="FacesDetectedUtc"/>, for the same reason.
    /// Five photographs in six carry no GPS at all, so a null
    /// <see cref="Latitude"/> cannot say whether the file was asked and had
    /// nothing or was never asked - and without somewhere to record the
    /// difference, nine thousand originals would be opened over the share on
    /// every run, for ever, to learn the same nothing.
    ///
    /// <para>Set once the answer is known, including when the answer is "no
    /// coordinates" or "coordinates too far from anywhere to name". All three are
    /// settled answers; none of them is worth asking the share again.</para>
    ///
    /// <para>Written only by the locating pass, which is the only thing that
    /// resolves a <see cref="PlaceId"/>. The preparing pass reads coordinates
    /// where it finds them but deliberately leaves this null, so that the place
    /// still gets named - it has no gazetteer and cannot finish the job it would
    /// be claiming to have finished.</para>
    ///
    /// <para>Cleared when the file's bytes change, exactly as the other derived
    /// facts are.</para>
    /// </remarks>
    public DateTime? LocationReadUtc { get; set; }

    /// <summary>Hex digest of the file's bytes. Only set for files that needed one.</summary>
    public string? ContentHash { get; set; }

    public PerceptualHash? PerceptualHash { get; set; }

    /// <summary>File name of the cached thumbnail, relative to the thumbnail store.</summary>
    public string? ThumbnailName { get; set; }

    /// <summary>
    /// When faces were last looked for in this photo, or null when they never
    /// have been.
    /// </summary>
    /// <remarks>
    /// A date of its own rather than another value on <see cref="Status"/>,
    /// which the preparing pass already owns and can only hold one answer at a
    /// time. It has to be recorded somewhere, because a photograph with no faces
    /// in it is otherwise indistinguishable from one that has never been looked
    /// at - and the difference is eleven thousand previews read again on every
    /// pass.
    ///
    /// <para>Cleared when the file's bytes change, exactly as the other derived
    /// facts are: the faces recorded belong to a picture that is no longer
    /// there.</para>
    /// </remarks>
    public DateTime? FacesDetectedUtc { get; set; }

    /// <summary>
    /// A quarter turn the user asked for, clockwise, on top of whatever the file
    /// itself said: 0, 90, 180 or 270.
    /// </summary>
    /// <remarks>
    /// EXIF orientation is already applied when a rendition is built, so this is
    /// only for pictures whose file does not say which way is up - a phone held
    /// upside down that wrote no tag, or a format that has nowhere to put one.
    /// Nobody but the person looking at it can tell.
    ///
    /// <para>Kept on the row rather than only in the rendition, because a
    /// rendition is derived and can be rebuilt at any time - by a changed file,
    /// a cleared cache, a new preview size. The turn is not derived from
    /// anything, so losing it would mean asking the user again.</para>
    /// </remarks>
    public int Rotation { get; set; }

    /// <summary>
    /// When this copy was set aside as redundant, or null while it is still in
    /// the library.
    /// </summary>
    /// <remarks>
    /// The file has been moved into the working folder's quarantine, not
    /// deleted, and this row is what makes putting it back possible. Keeping the
    /// row rather than removing it also stops the next scan from concluding the
    /// file has gone and taking the row - and with it any names confirmed on the
    /// faces in it - away for good.
    ///
    /// <para>Everything that shows pictures filters these out. They are not in
    /// the library any more; they are recoverable.</para>
    /// </remarks>
    public DateTime? QuarantinedUtc { get; set; }

    /// <summary>
    /// The top-level folder, which in this library names the event
    /// ("20230203 - Chingay") or just the month ("20230201").
    /// </summary>
    public string TopFolder
    {
        get
        {
            int separator = RelativePath.IndexOfAny(s_pathSeparators);
            return separator < 0 ? string.Empty : RelativePath[..separator];
        }
    }

    /// <summary>How many folders deep the file sits below the library root.</summary>
    public int Depth => RelativePath.Count(c => c is '\\' or '/');

    private static readonly char[] s_pathSeparators = ['\\', '/'];
}

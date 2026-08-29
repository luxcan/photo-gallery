using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// What one face is called on two machines: its photograph, and the box the
/// detector drew round it.
/// </summary>
/// <remarks>
/// The box rather than an ordinal. Two machines running the same detector over
/// the same rendition produce the same rectangles - <c>OnnxFaceScanner</c>
/// appends no execution provider, so every machine runs the same CPU graph over
/// a preview this app wrote itself - while the order results were collected in
/// is an artefact of the collecting and is not a fact about anybody's face.
///
/// <para><strong>Only true after rotations have merged.</strong> Turning a
/// photograph rewrites every face's bounds, so a box means nothing on its own:
/// confirm a name, turn the photo, and the box that was X is now Y while the
/// machine that never turned it still holds X. Once both machines have agreed
/// the turn and moved their own boxes through the same arithmetic they are in
/// the same frame, and this plain key matches exactly. An ordering rule instead
/// of a wider key.</para>
///
/// <para>The day a GPU provider is added the rectangles stop being reproducible
/// - floating point moves a box by a pixel - and matching degrades quietly to
/// the overlap fallback. Recorded as a standing risk rather than discovered.</para>
/// </remarks>
public readonly record struct FaceKey(AssetKey Photo, FaceBounds Bounds)
{
    /// <summary>
    /// How the box is written down where it has to be one short string: the
    /// part of a photograph a held answer is about.
    /// </summary>
    public string Part => $"{Bounds.X},{Bounds.Y},{Bounds.Width},{Bounds.Height}";

    /// <summary>The key as one string, which is how it is written to a file.</summary>
    /// <remarks>
    /// A vertical bar separates the photograph from the box, for the reason
    /// <see cref="AssetKey.ToString"/> uses a colon: Windows will not have one in
    /// a path, so the half that is free text cannot contain the separator.
    /// </remarks>
    public override string ToString() => $"{Photo}|{Part}";

    /// <summary>Reads back what <see cref="ToString"/> wrote.</summary>
    /// <exception cref="FormatException">The text is not a key.</exception>
    public static FaceKey Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int cut = text.LastIndexOf('|');
        if (cut <= 0)
        {
            throw new FormatException($"Not a face key: {text}");
        }

        string[] box = text[(cut + 1)..].Split(',');
        if (box.Length != 4 || !box.All(part => int.TryParse(part, out _)))
        {
            throw new FormatException($"Not a face key: {text}");
        }

        return new FaceKey(
            AssetKey.Parse(text[..cut]),
            new FaceBounds(
                int.Parse(box[0]), int.Parse(box[1]), int.Parse(box[2]), int.Parse(box[3])));
    }
}

using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// A face one machine's detector found, offered to the others whole - box,
/// score and vector.
/// </summary>
/// <remarks>
/// <strong>A face vector arrives as a face, or the two hours are not saved.</strong>
/// This is the one place the pool's own rule - fill in rows, never create them -
/// does not transfer, and saying so is the difference between the 40.5 MB buying
/// something and buying nothing. A machine that has never run detection has no
/// face rows at all, so there is nothing for a vector to attach to; if it does
/// not create them it runs the full pass anyway and the transfer was decoration.
///
/// <para>The asset rule and the face rule differ because the questions differ. A
/// rendition without a row would be a picture whose original the app cannot
/// reach - a new state every screen would have to learn. A face without a row is
/// just a face, in a photograph this machine already has, found by the same
/// model over the same pixels.</para>
/// </remarks>
public sealed record SharedFace(FaceKey Face, float DetectScore, FaceEmbedding Embedding);

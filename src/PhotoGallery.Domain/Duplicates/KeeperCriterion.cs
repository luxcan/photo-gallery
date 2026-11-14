namespace PhotoGallery.Domain.Duplicates;

/// <summary>
/// One rung of the ladder that suggests which copy of a picture to keep, in the
/// order the rungs are tried (PRP 14).
/// </summary>
/// <remarks>
/// There is deliberately no rung after <see cref="Pixels"/>. The folder rule that
/// used to sit there said nothing about the picture, so a suggestion it decided
/// was a guess dressed as a judgement; when every rung ties, nothing is
/// suggested.
///
/// <para>Bytes are not a rung either. Bytes were meant to settle one photograph
/// saved twice, but no signal in the library tells that apart from two frames
/// of a burst - capture times agree to the second within a burst, and most
/// copies carry none - and on the real library bytes chose between plainly
/// different frames in five of six sets looked at.</para>
/// </remarks>
public enum KeeperCriterion
{
    /// <summary>Fewer known people with their eyes closed.</summary>
    Eyes = 0,

    /// <summary>The clearly sharper picture.</summary>
    Sharpness = 1,

    /// <summary>Clearly less of the picture blown white or crushed black.</summary>
    Exposure = 2,

    /// <summary>The copy that still carries the camera's details.</summary>
    Original = 3,

    /// <summary>More pixels.</summary>
    Pixels = 4,
}

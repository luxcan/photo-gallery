namespace PhotoGallery.Domain.Sharing;

/// <summary>What a held answer is about, and so what applying it means.</summary>
public enum HeldDecisionKind
{
    /// <summary>Somebody named, or refused, the face at a given box.</summary>
    FaceAnswer = 0,

    /// <summary>Somebody said which way up the photograph goes.</summary>
    Turn = 1,

    /// <summary>Somebody put the photograph in an album.</summary>
    AlbumMembership = 2,

    /// <summary>Somebody said the photograph does not belong in a run of days.</summary>
    AlbumRejection = 3,

    /// <summary>Somebody said the face at a given box is nobody worth tracking.</summary>
    /// <remarks>
    /// Its own kind rather than a <see cref="FaceAnswer"/> with no name in it.
    /// The two are settled against each other when they land - a mark that came
    /// later than a name wins, and the other way round - but while they wait
    /// they are different answers and both have to survive. Sharing one kind
    /// would make the row that stored them ambiguous to read back, and reading
    /// them back is the whole point of holding them.
    /// </remarks>
    Stranger = 4,
}

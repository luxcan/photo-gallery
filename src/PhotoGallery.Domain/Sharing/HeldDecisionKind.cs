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
}

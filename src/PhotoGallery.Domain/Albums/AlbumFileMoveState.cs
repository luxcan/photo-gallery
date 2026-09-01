namespace PhotoGallery.Domain.Albums;

/// <summary>How far one original has got through an album folder move.</summary>
public enum AlbumFileMoveState
{
    /// <summary>The intended destination is durable, but the file has not moved yet.</summary>
    Planned = 0,

    /// <summary>The file is at its destination and the asset row still needs settling.</summary>
    FileMoved = 1,

    /// <summary>The file and the asset's relative path both name the destination.</summary>
    Completed = 2,

    /// <summary>This file was left alone, or could not be settled automatically.</summary>
    Failed = 3,
}

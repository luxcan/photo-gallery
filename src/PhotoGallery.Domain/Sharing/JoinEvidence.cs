namespace PhotoGallery.Domain.Sharing;

/// <summary>Why two people were offered as one.</summary>
public enum JoinEvidence
{
    /// <summary>Their faces agree, which is the app's own measure of who somebody is.</summary>
    TheyLookAlike = 0,

    /// <summary>They were given the same name on two machines.</summary>
    SameName = 1,

    /// <summary>Both, which is as sure as this can be without asking.</summary>
    Both = 2,
}

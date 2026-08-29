namespace PhotoGallery.Domain.Sharing;

/// <summary>Two people who look like one, offered rather than joined.</summary>
/// <remarks>
/// Two machines that each created "Ana" independently produce two identities,
/// and two Anas is a real thing in a family - so the merge must not join them.
/// What it can do is notice, and say so on screen with the faces to look at.
/// </remarks>
public sealed record PersonJoin(Guid Left, Guid Right, JoinEvidence Because, float Similarity);

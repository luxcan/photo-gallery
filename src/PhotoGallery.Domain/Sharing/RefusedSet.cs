namespace PhotoGallery.Domain.Sharing;

/// <summary>A machine whose answers were not taken, and why.</summary>
/// <param name="Detail">
/// What to put on screen after the reason: how far ahead the clock is, which
/// release it is running. A refusal nobody can act on is a refusal that reads as
/// a fault in this app.
/// </param>
public sealed record RefusedSet(MachineIdentity Machine, RefusalReason Reason, string Detail);

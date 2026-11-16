namespace PhotoGallery.App.Sharing;

/// <summary>
/// A computer this one has not synced with before, as the screen asks about
/// it.
/// </summary>
/// <remarks>
/// Asked by name because a name is all a person can judge - and warned when the
/// name is one an already trusted computer uses, because a file in the sync
/// folder can call itself anything, including the laptop on the kitchen table.
/// </remarks>
public sealed record MachineQuestion(Guid MachineId, string Name, string Question);

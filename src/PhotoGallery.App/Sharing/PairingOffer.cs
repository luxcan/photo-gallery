namespace PhotoGallery.App.Sharing;

/// <summary>
/// A folder on another computer that might be a folder here, as the screen asks
/// it.
/// </summary>
/// <remarks>
/// The question is put in the two roots and nothing else, because those are the
/// only part of it a person can actually judge. The ids are carried so the
/// answer can be recorded, and are never shown: a family deciding whether
/// <c>Z:\PhotoGallery</c> is the drive on the landing does not need two
/// <see cref="Guid"/> values to do it.
/// </remarks>
public sealed record PairingOffer(Guid Mine, Guid Theirs, string Question);

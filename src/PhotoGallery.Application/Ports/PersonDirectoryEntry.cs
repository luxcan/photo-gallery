namespace PhotoGallery.Application.Ports;

/// <summary>
/// A name that can be searched for, and how many pictures it would return.
/// </summary>
/// <remarks>
/// Deliberately without a face or a vector. This answers a search box on every
/// keystroke, and the full picture of a person costs every embedding in the
/// library; the count is what a searcher actually wants to see before pressing
/// Enter.
/// </remarks>
public sealed record PersonDirectoryEntry(int Id, string DisplayName, int Photos);

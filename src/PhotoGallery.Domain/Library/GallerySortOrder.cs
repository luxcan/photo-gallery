namespace PhotoGallery.Domain.Library;

/// <summary>Which end of the library the grid starts at.</summary>
/// <remarks>
/// An enum rather than a bool because the order is stored, and a column of
/// zeroes and ones tells a later reader nothing. <see cref="NewestFirst"/> is
/// deliberately zero: it is the order the grid has always used, so a row written
/// before this existed already holds the right answer.
/// </remarks>
public enum GallerySortOrder
{
    NewestFirst = 0,
    OldestFirst = 1,
}

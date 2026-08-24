using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>
/// Cuts somebody's pictures into the ages they were, keeping the order the
/// reader chose.
/// </summary>
/// <remarks>
/// <para>
/// The date used is <see cref="GalleryItem.SortedOn"/> - the capture date where
/// the file carries one, the file's own date where it does not. Grouping on the
/// capture date alone was the other candidate and was rejected on the library's
/// own numbers: three of the fifteen people named here have almost no capture
/// dates at all (89 of Noor's 89 pictures carry none), so that rule would
/// have shown those people a single "date not known" heading and nothing else.
/// </para>
/// <para>
/// The cost is that a file date is only ever later than the moment the shutter
/// fired, so an age read off one is never too young and may be too old. That is
/// why <see cref="AgeGroup.DatedFromTheFile"/> is counted here and said on the
/// screen, instead of the ages being quietly presented as measured.
/// </para>
/// <para>
/// One walk, not a <c>GroupBy</c>. The reader returns rows already ordered by
/// date, so ages change monotonically and a run is contiguous - and walking
/// preserves the reader's own tie-break between rows sharing a date, which
/// regrouping would silently reorder.
/// </para>
/// </remarks>
public static class PersonPhotoGrouping
{
    /// <summary>
    /// The pictures cut into age groups, or a single unheaded group when no year
    /// of birth has been given.
    /// </summary>
    public static IReadOnlyList<AgeGroup> Into(
        IReadOnlyList<GalleryItem> photos, int? birthYear)
    {
        ArgumentNullException.ThrowIfNull(photos);

        if (photos.Count == 0)
        {
            return [];
        }

        if (birthYear is null)
        {
            return [new AgeGroup(null, null, CountInferred(photos), photos)];
        }

        var groups = new List<AgeGroup>();
        var run = new List<GalleryItem>();
        int runBucket = 0;
        int inferred = 0;

        foreach (GalleryItem photo in photos)
        {
            int bucket = PersonAge.Bucket(PersonAge.At(birthYear, photo.SortedOn)!.Value);

            if (run.Count > 0 && bucket != runBucket)
            {
                groups.Add(Close(runBucket, inferred, run));
                run = [];
                inferred = 0;
            }

            runBucket = bucket;
            run.Add(photo);
            if (photo.TakenUtc is null)
            {
                inferred++;
            }
        }

        groups.Add(Close(runBucket, inferred, run));
        return groups;
    }

    private static AgeGroup Close(int bucket, int inferred, List<GalleryItem> run) =>
        new(bucket, PersonAge.Heading(bucket), inferred, run);

    private static int CountInferred(IReadOnlyList<GalleryItem> photos)
    {
        int inferred = 0;
        foreach (GalleryItem photo in photos)
        {
            if (photo.TakenUtc is null)
            {
                inferred++;
            }
        }

        return inferred;
    }
}

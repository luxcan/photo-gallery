using System.Globalization;
using System.Windows.Media;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.App.Collections;

/// <summary>One collection as a row in the list.</summary>
/// <remarks>
/// Display only. The span and the count are a caption rather than part of the
/// name, so renaming replaces a title and never has to reproduce a date range.
/// </remarks>
public sealed record CollectionItem(CollectionSummary Summary, ImageSource? Cover)
{
    public int Id => Summary.Id;

    public string Name => Summary.Name;

    public bool IsProposed => Summary.Origin == CollectionOrigin.Proposed;

    public bool IsMine => Summary.Origin != CollectionOrigin.Proposed;

    /// <summary>
    /// What sort of occasion, in one word, or nothing when the word would say
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Only a trip earns a badge. Marking every card "Day" or "Days" tells the
    /// reader what the caption underneath already says, and a badge that is
    /// always there stops being a badge.
    /// </remarks>
    public string KindLabel => Summary.Kind == CollectionKind.Trip ? "Trip" : string.Empty;

    public bool HasKindLabel => KindLabel.Length > 0;

    /// <summary>How many photographs, and when they were taken.</summary>
    public string Caption =>
        $"{Photos}{(Span.Length > 0 ? $", {Span}" : string.Empty)}";

    private string Photos =>
        Summary.PhotoCount == 1 ? "1 photo" : $"{Summary.PhotoCount:N0} photos";

    /// <summary>
    /// The days it covers, written the way a person says them.
    /// </summary>
    /// <remarks>
    /// Formatted from the wall-clock value the camera wrote. A capture time
    /// carries no offset, so converting it would move the date by whatever the
    /// machine's timezone happens to be - and a photograph taken at nine in the
    /// evening would show up under the following day.
    /// </remarks>
    private string Span
    {
        get
        {
            if (Summary.Origin == CollectionOrigin.Made && Summary.PhotoCount == 0)
            {
                return string.Empty;
            }

            DateTime start = Summary.StartUtc;
            DateTime end = Summary.EndUtc;

            if (start.Date == end.Date)
            {
                return start.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
            }

            return start.Year == end.Year && start.Month == end.Month
                ? $"{start.Day}-{end.ToString("d MMM yyyy", CultureInfo.CurrentCulture)}"
                : $"{start.ToString("d MMM", CultureInfo.CurrentCulture)} - "
                  + $"{end.ToString("d MMM yyyy", CultureInfo.CurrentCulture)}";
        }
    }
}

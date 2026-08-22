using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.Ports;

/// <summary>What the gallery is asking to see.</summary>
/// <param name="PhotoSourceId">
/// Required alongside <paramref name="FolderPath"/>: two sources may legitimately
/// hold a folder of the same name, and a folder without its source is ambiguous.
/// </param>
/// <param name="FolderPath">Restricts to one folder and everything beneath it.</param>
/// <param name="IncludeVideos">
/// Whether videos join the photographs - and when they do, only the videos that
/// have a poster.
///
/// <para>The library view asked for photographs only for as long as a video was
/// a grey square: 4,743 of them interleaved by date buries the photographs
/// without making a single video findable. [08] answered that, and the answer is
/// per file rather than per library. A video is shown once it has a picture on
/// it and not before, which is deliberately unlike a photograph - a photograph's
/// placeholder is filled in by the preparing pass that runs straight after the
/// scan, where a video waits on a long pass somebody has to choose to start, and
/// could sit grey for months.</para>
/// </param>
/// <param name="Take">
/// Zero means everything. The grid takes everything - it virtualises rows, so
/// paging would cost more than it saves - but the one-photo view and the search
/// that comes later both want a slice.
/// </param>
/// <param name="SortOrder">
/// Which end of the library to start at. Defaults to newest first, which is what
/// every caller wanted before the grid grew a control for it.
/// </param>
/// <param name="PersonId">
/// Restricts to pictures somebody is in. Only confirmed faces count: a proposal
/// is a question the user has not answered, and answering it by quietly
/// including the picture would make the question pointless.
/// </param>
/// <param name="Place">
/// Restricts to photographs taken somewhere - one gazetteer place, or anywhere
/// in one country. Composes with everything else rather than replacing it, so a
/// person and a place together are that person's photographs there.
/// </param>
/// <param name="RankedAssetIds">
/// Restricts to these photographs and shows them in this order, which is how a
/// typed description is answered: the ranking is the answer, so date order would
/// throw away the only thing that made these pictures the ones offered.
/// Everything else still applies - a folder, a source, a person - so a
/// description narrows what is already on screen rather than replacing it.
/// </param>
public sealed record GalleryQuery(
    int? PhotoSourceId = null,
    string? FolderPath = null,
    bool IncludeVideos = true,
    int Skip = 0,
    int Take = 0,
    GallerySortOrder SortOrder = GallerySortOrder.NewestFirst,
    int? PersonId = null,
    IReadOnlyList<int>? RankedAssetIds = null,
    PlaceFilter? Place = null);

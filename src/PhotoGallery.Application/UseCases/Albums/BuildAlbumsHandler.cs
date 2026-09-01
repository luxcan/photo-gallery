using System.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Application.UseCases.Albums;

/// <summary>
/// Groups the library into occasions and offers them.
/// </summary>
/// <remarks>
/// The cheapest phase in a scan and the last one, because it reads what every
/// other phase writes: capture dates from the preparing pass, places from the
/// locating pass, people from the faces pass. Nothing here decodes an image or
/// runs a model - it is one sorted pass over the dates the index already holds.
///
/// <para><strong>It only ever revises its own suggestions.</strong> An album
/// the user kept or made is not in what it reads and not in what it writes.
/// That is the difference between offering and imposing, and it is why a rebuild
/// after adding a folder does not disturb anything somebody has organised.</para>
///
/// <para><strong>A rejection is honoured before a proposal is made, not
/// after.</strong> The photographs the user has refused for a span are dropped
/// from that span's group, and the group then has to earn its place again on
/// what is left - so dismissing an occasion keeps it dismissed without any
/// separate flag, and a span that later gains eight genuinely new photographs is
/// offered afresh, which is correct.</para>
/// </remarks>
public sealed class BuildAlbumsHandler
{
    private readonly IAlbumRepository _albums;
    private readonly IAlbumFactsReader _facts;

    public BuildAlbumsHandler(
        IAlbumRepository albums, IAlbumFactsReader facts)
    {
        _albums = albums;
        _facts = facts;
    }

    public async Task<AlbumsResult> HandleAsync(
        IProgress<AlbumsProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<DatedPhoto> candidates;
        IReadOnlyDictionary<string, IReadOnlyList<int>> rejected;
        try
        {
            candidates = await _albums
                .GetCandidatesAsync(cancellationToken).ConfigureAwait(false);
            rejected = await _albums
                .GetRejectionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Answered rather than thrown. This runs as the last phase of a
            // scan, where an exception has nowhere to go but the dispatcher.
            return AlbumsResult.Stopped(stopwatch.Elapsed);
        }

        if (candidates.Count == 0)
        {
            return new AlbumsResult(0, 0, 0, stopwatch.Elapsed, WasCancelled: false);
        }

        progress?.Report(new AlbumsProgress(0, candidates.Count));

        IReadOnlyList<PhotoGroup> groups = AlbumClusterer.Group(candidates);
        var proposals = new List<ProposedAlbum>(groups.Count);
        int grouped = 0;

        foreach (PhotoGroup group in groups)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return AlbumsResult.Stopped(stopwatch.Elapsed);
            }

            IReadOnlyList<int> members = Keeping(group, rejected);
            if (members.Count < AlbumClusterer.FewestPhotos)
            {
                // What the user has refused has been taken out, and what is left
                // no longer amounts to an occasion. This is how a dismissal
                // stays dismissed.
                continue;
            }

            AlbumFacts facts = await _facts
                .DescribeAsync(group, members, cancellationToken).ConfigureAwait(false);

            proposals.Add(new ProposedAlbum(
                group.Key,
                AlbumNamer.Name(facts),
                group.StartUtc,
                group.EndUtc,
                facts.Kind,
                await _facts.PlaceOfAsync(members, cancellationToken).ConfigureAwait(false),
                await _facts.CoverOfAsync(members, cancellationToken).ConfigureAwait(false),
                members));

            grouped += members.Count;
            progress?.Report(new AlbumsProgress(proposals.Count, groups.Count));
        }

        int written = await _albums
            .SaveProposalsAsync(proposals, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        return new AlbumsResult(
            written, grouped, candidates.Count, stopwatch.Elapsed, WasCancelled: false);
    }

    /// <summary>The group's photographs, minus the ones refused for this span.</summary>
    private static IReadOnlyList<int> Keeping(
        PhotoGroup group, IReadOnlyDictionary<string, IReadOnlyList<int>> rejected)
    {
        if (!rejected.TryGetValue(group.Key, out IReadOnlyList<int>? refused))
        {
            return group.AssetIds;
        }

        var out_ = new HashSet<int>(refused);

        return [.. group.AssetIds.Where(assetId => !out_.Contains(assetId))];
    }
}

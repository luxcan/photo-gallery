using System.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Application.UseCases.Collections;

/// <summary>
/// Groups the library into occasions and offers them.
/// </summary>
/// <remarks>
/// The cheapest phase in a scan and the last one, because it reads what every
/// other phase writes: capture dates from the preparing pass, places from the
/// locating pass, people from the faces pass. Nothing here decodes an image or
/// runs a model - it is one sorted pass over the dates the index already holds.
///
/// <para><strong>It only ever revises its own suggestions.</strong> A collection
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
public sealed class BuildCollectionsHandler
{
    private readonly ICollectionRepository _collections;
    private readonly ICollectionFactsReader _facts;

    public BuildCollectionsHandler(
        ICollectionRepository collections, ICollectionFactsReader facts)
    {
        _collections = collections;
        _facts = facts;
    }

    public async Task<CollectionsResult> HandleAsync(
        IProgress<CollectionsProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<DatedPhoto> candidates;
        IReadOnlyDictionary<string, IReadOnlyList<int>> rejected;
        try
        {
            candidates = await _collections
                .GetCandidatesAsync(cancellationToken).ConfigureAwait(false);
            rejected = await _collections
                .GetRejectionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Answered rather than thrown. This runs as the last phase of a
            // scan, where an exception has nowhere to go but the dispatcher.
            return CollectionsResult.Stopped(stopwatch.Elapsed);
        }

        if (candidates.Count == 0)
        {
            return new CollectionsResult(0, 0, 0, stopwatch.Elapsed, WasCancelled: false);
        }

        progress?.Report(new CollectionsProgress(0, candidates.Count));

        IReadOnlyList<PhotoGroup> groups = CollectionClusterer.Group(candidates);
        var proposals = new List<ProposedCollection>(groups.Count);
        int grouped = 0;

        foreach (PhotoGroup group in groups)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CollectionsResult.Stopped(stopwatch.Elapsed);
            }

            IReadOnlyList<int> members = Keeping(group, rejected);
            if (members.Count < CollectionClusterer.FewestPhotos)
            {
                // What the user has refused has been taken out, and what is left
                // no longer amounts to an occasion. This is how a dismissal
                // stays dismissed.
                continue;
            }

            CollectionFacts facts = await _facts
                .DescribeAsync(group, members, cancellationToken).ConfigureAwait(false);

            proposals.Add(new ProposedCollection(
                group.Key,
                CollectionNamer.Name(facts),
                group.StartUtc,
                group.EndUtc,
                facts.Kind,
                await _facts.PlaceOfAsync(members, cancellationToken).ConfigureAwait(false),
                members[members.Count / 2],
                members));

            grouped += members.Count;
            progress?.Report(new CollectionsProgress(proposals.Count, groups.Count));
        }

        int written = await _collections
            .SaveProposalsAsync(proposals, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        return new CollectionsResult(
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

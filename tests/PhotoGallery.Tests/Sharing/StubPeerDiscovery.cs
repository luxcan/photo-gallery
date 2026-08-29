using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing.Direct;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Stands in for finding the other computers on the network.
/// </summary>
/// <remarks>
/// The real one asks Windows about the adapters this machine is on, which is a
/// fact about whatever ran the tests. What is worth asserting is that the answer
/// reaches the screen - so this says whatever a test needs it to.
/// </remarks>
internal sealed class StubPeerDiscovery : IPeerDiscovery
{
    private readonly List<Beacon> _found = [];

    private DiscoveryProblem _problem = DiscoveryProblem.None;

    /// <summary>Says the network will not carry the beacon, and why.</summary>
    public StubPeerDiscovery Blocked(DiscoveryProblem problem)
    {
        _problem = problem;
        return this;
    }

    /// <summary>Says this machine is out there and would answer.</summary>
    public StubPeerDiscovery Holding(Beacon beacon)
    {
        _found.Add(beacon);
        return this;
    }

    public Task<DiscoveryProblem> ReadinessAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_problem);

    public Task<IReadOnlyList<Beacon>> LookAsync(
        Beacon mine, TimeSpan listenFor, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Beacon>>(
            _problem == DiscoveryProblem.None ? _found : []);
}

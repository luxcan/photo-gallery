using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.App.Sharing;

/// <summary>
/// This machine's ear on the family network, open for as long as the app is.
/// </summary>
/// <remarks>
/// <strong>Listening is always on; announcing is not.</strong> A machine that
/// only listened when somebody was looking at the Sharing screen would make
/// pairing a two-person job - both screens open at the same moment - and a
/// machine that announced itself for ever is not something to put on somebody
/// else's laptop. So this waits quietly, and the person who wants to share is
/// the one who calls.
///
/// <para>What it will say to a stranger is its name. Everything else - which is
/// to say every decision anybody in the house has made - needs a fingerprint
/// this library has already agreed to.</para>
/// </remarks>
public sealed class DirectSharing : IAsyncDisposable, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private PeerListener? _listener;

    public DirectSharing(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>The port this machine is reachable on, or 0 when it is not.</summary>
    public int Port => _listener?.Port ?? 0;

    /// <summary>
    /// Six digits this machine will accept, while the Sharing screen is showing
    /// them.
    /// </summary>
    /// <remarks>
    /// Cleared as soon as one pairing uses it. The code was read aloud across a
    /// room and is no more secret than that; leaving it live afterwards would
    /// make every later pairing free to whoever overheard it.
    /// </remarks>
    public string? Offering
    {
        get => _listener?.Offering;
        set
        {
            if (_listener is not null)
            {
                _listener.Offering = value;
            }
        }
    }

    /// <summary>Starts listening, and answers whether it could.</summary>
    /// <remarks>
    /// Failure here is not fatal and is not reported: a machine that cannot
    /// listen still shares perfectly well through a folder, which is how almost
    /// everybody uses this. The screen says so when somebody asks it to find
    /// another computer.
    /// </remarks>
    public bool Start()
    {
        if (_listener is not null)
        {
            return true;
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var certificate = scope.ServiceProvider.GetRequiredService<PeerCertificate>();

            _listener = new PeerListener(certificate, MineAsync, Machine, IsPaired);
            _listener.Start();

            _listener.Paired += (_, paired) => Remember(paired);

            return true;
        }
        catch (Exception ex) when (ex is IOException
                                      or InvalidOperationException
                                      or System.Net.Sockets.SocketException)
        {
            _listener = null;
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_listener is not null)
        {
            await _listener.DisposeAsync().ConfigureAwait(false);
            _listener = null;
        }
    }

    /// <summary>
    /// The synchronous half, which the container insists on having.
    /// </summary>
    /// <remarks>
    /// A singleton that implements only <see cref="IAsyncDisposable"/> makes
    /// <c>ServiceProvider.Dispose()</c> throw - and that is exactly what closing
    /// the window does. Waiting here is safe: what it waits for is a listening
    /// socket being told to stop, which it does at once.
    /// </remarks>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// What this library has decided, read fresh for every machine that asks.
    /// </summary>
    /// <remarks>
    /// Not cached. A decision set is what this library holds <em>now</em>, and a
    /// peer that connected an hour after the last read would be handed an hour
    /// old answer with nothing to say so.
    /// </remarks>
    private async Task<DecisionSet> MineAsync()
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        var index = scope.ServiceProvider.GetRequiredService<ILibraryIndex>();
        var decisions = scope.ServiceProvider.GetRequiredService<IDecisionReader>();

        MachineIdentity me = await PublishDecisionsHandler
            .ThisMachineAsync(index, CancellationToken.None)
            .ConfigureAwait(false);

        return await decisions.ReadAsync(me, CancellationToken.None).ConfigureAwait(false);
    }

    private Guid Machine()
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var index = scope.ServiceProvider.GetRequiredService<ILibraryIndex>();

        return PublishDecisionsHandler
            .ThisMachineAsync(index, CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Id;
    }

    /// <summary>
    /// Whether a certificate is one this library has agreed to.
    /// </summary>
    /// <remarks>
    /// Asked per request rather than captured once, because pairing happens
    /// while this is running - and a machine paired a moment ago should not have
    /// to wait for a restart to be believed.
    /// </remarks>
    private bool IsPaired(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var decisions = scope.ServiceProvider.GetRequiredService<IDecisionReader>();

            return decisions.PeersAsync().GetAwaiter().GetResult().Any(peer =>
                string.Equals(peer.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            // No library open yet. Nobody is paired with a library that is not
            // there, which is the right answer as well as the safe one.
            return false;
        }
    }

    private void Remember(PeerPairing paired)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDecisionRepository>();

            repository
                .RememberAsync(
                    new MachineIdentity(
                        paired.MachineId, paired.Name, string.Empty, SharingVersion.Schema),
                    DateTime.UtcNow)
                .GetAwaiter()
                .GetResult();
        }
        catch (InvalidOperationException)
        {
            // The pairing still happened and the other machine still knows. What
            // is lost is this end remembering it, which the user can redo.
        }
    }
}

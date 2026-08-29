using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Domain.Sharing.Direct;
using PhotoGallery.Infrastructure.Sharing;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Two machines finding each other with no folder in common, pairing, and
/// actually exchanging answers over the wire.
/// </summary>
/// <remarks>
/// Over the loopback, which is a real TLS connection with real certificates -
/// the only part these cannot exercise is whether the family's Wi-Fi will carry
/// a multicast packet, and no test anywhere can answer that.
/// </remarks>
public sealed class DirectConnectionTests : IAsyncDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"pg-direct-{Guid.NewGuid():N}");

    private readonly List<PeerListener> _listening = [];

    [Fact]
    public void ACertificateIsMadeOnceAndKept()
    {
        // The fingerprint is the identity. One made per run would mean pairing
        // again per run, which is a feature nobody would use twice.
        PeerCertificate first = Certificate("mum");
        string once = first.Fingerprint();

        PeerCertificate again = Certificate("mum");

        Assert.Equal(once, again.Fingerprint());
        Assert.Equal(64, once.Length);
    }

    [Fact]
    public void TwoMachinesHaveDifferentIdentities()
    {
        Assert.NotEqual(Certificate("mum").Fingerprint(), Certificate("dad").Fingerprint());
    }

    [Fact]
    public async Task AMachineSaysWhoItIsWithoutBeingPaired()
    {
        // The one thing it will say to a stranger. Everything else needs a
        // fingerprint this library has already agreed to.
        (PeerListener hers, PeerCertificate herCert) = Listen("mum", Set("Mum's laptop"));

        var link = new PeerLink(Certificate("dad"));
        PeerFound greeting = await link.GreetAsync("127.0.0.1", hers.Port, Me("Dad"));

        Assert.Equal(herCert.Fingerprint(), greeting.Fingerprint);
        Assert.Equal(SharingVersionSchema, greeting.SchemaVersion);
    }

    [Fact]
    public async Task AnUnpairedMachineIsRefusedTheAnswers()
    {
        (PeerListener hers, _) = Listen("mum", Set("Mum's laptop"));

        var link = new PeerLink(Certificate("dad"));

        DecisionSet? got = await link.AskAsync(
            "127.0.0.1", hers.Port, Me("Dad"), Certificate("mum").Fingerprint());

        Assert.Null(got);
    }

    [Fact]
    public async Task PairingWithTheRightCodeSucceedsAndTheAnswersFollow()
    {
        PeerCertificate hersCert = Certificate("mum");
        DecisionSet mine = Set("Mum's laptop");

        HashSet<string> paired = new(StringComparer.OrdinalIgnoreCase);
        (PeerListener hers, _) = Listen("mum", mine, paired);

        string code = PairingCode.Mint();
        hers.Offering = code;
        hers.Paired += (_, outcome) => paired.Add(outcome.Fingerprint);

        var link = new PeerLink(Certificate("dad"));

        PeerPairing outcome = await link.PairAsync(
            "127.0.0.1", hers.Port, Me("Dad"), code);

        Assert.True(outcome.Succeeded);
        Assert.Equal(hersCert.Fingerprint(), outcome.Fingerprint);

        // And now the answers cross.
        DecisionSet? got = await link.AskAsync(
            "127.0.0.1", hers.Port, Me("Dad"), outcome.Fingerprint);

        Assert.NotNull(got);
        Assert.Equal("Mum's laptop", got.Machine.Name);
        Assert.Single(got.People);
    }

    [Fact]
    public async Task TheWrongCodeIsRefusedAndSaysSoWithoutSayingWhichPart()
    {
        (PeerListener hers, _) = Listen("mum", Set("Mum's laptop"));
        hers.Offering = "123456";

        var link = new PeerLink(Certificate("dad"));

        PeerPairing outcome = await link.PairAsync(
            "127.0.0.1", hers.Port, Me("Dad"), "654321");

        Assert.False(outcome.Succeeded);
        Assert.Contains("did not match", outcome.Problem);
    }

    [Fact]
    public async Task AMachineThatIsNotOfferingWillNotPairAtAll()
    {
        // A machine that would pair at any time is one anybody on the Wi-Fi can
        // guess their way into at their leisure - a million tries, unattended.
        (PeerListener hers, _) = Listen("mum", Set("Mum's laptop"));

        Assert.Null(hers.Offering);

        var link = new PeerLink(Certificate("dad"));

        PeerPairing outcome = await link.PairAsync(
            "127.0.0.1", hers.Port, Me("Dad"), PairingCode.Mint());

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public async Task ACodeIsSpentOnceItHasBeenUsed()
    {
        // It was read aloud across a room and is no more secret than that.
        HashSet<string> paired = new(StringComparer.OrdinalIgnoreCase);
        (PeerListener hers, _) = Listen("mum", Set("Mum's laptop"), paired);

        string code = PairingCode.Mint();
        hers.Offering = code;
        hers.Paired += (_, outcome) => paired.Add(outcome.Fingerprint);

        var dad = new PeerLink(Certificate("dad"));
        Assert.True((await dad.PairAsync("127.0.0.1", hers.Port, Me("Dad"), code)).Succeeded);

        // Somebody who overheard it tries the same six digits.
        var stranger = new PeerLink(Certificate("stranger"));
        PeerPairing second = await stranger.PairAsync(
            "127.0.0.1", hers.Port, Me("Stranger"), code);

        Assert.False(second.Succeeded);
    }

    [Fact]
    public async Task AFingerprintThatHasChangedIsRefusedRatherThanSilentlyAccepted()
    {
        (PeerListener hers, _) = Listen("mum", Set("Mum's laptop"));

        var link = new PeerLink(Certificate("dad"));

        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(
            () => link.AskAsync(
                "127.0.0.1", hers.Port, Me("Dad"), Certificate("someone-else").Fingerprint()));
    }

    // ------------------------------------------------------------- the maths

    [Fact]
    public void BothEndsDeriveTheSameCheckValueWhicheverWayRound()
    {
        // There is no first machine anywhere else in this feature, and no reason
        // to invent one here.
        string one = PairingCode.Check("123456", "aaaa", "bbbb");
        string other = PairingCode.Check("123456", "bbbb", "aaaa");

        Assert.Equal(one, other);
    }

    [Fact]
    public void AMachineInTheMiddleCannotProduceEitherEndsCheckValue()
    {
        // Its certificate is its own, so the value it can compute is a value
        // about itself - and both ends see that it is not theirs.
        string real = PairingCode.Check("123456", "mum", "dad");
        string middle = PairingCode.Check("123456", "mum", "attacker");

        Assert.NotEqual(real, middle);
        Assert.False(PairingCode.Matches(real, middle));
    }

    [Fact]
    public void ACodeIsSixDigitsAndNothingElseIsAccepted()
    {
        Assert.True(PairingCode.IsWellFormed(PairingCode.Mint()));
        Assert.True(PairingCode.IsWellFormed("000000"));
        Assert.False(PairingCode.IsWellFormed("12345"));
        Assert.False(PairingCode.IsWellFormed("1234567"));
        Assert.False(PairingCode.IsWellFormed("12345a"));
        Assert.False(PairingCode.IsWellFormed(null));
    }

    [Fact]
    public void AMintedCodeIsAlwaysSixDigits()
    {
        for (int i = 0; i < 200; i++)
        {
            string code = PairingCode.Mint();
            Assert.Equal(6, code.Length);
            Assert.True(code.All(char.IsAsciiDigit));
        }
    }

    // ------------------------------------------------------------- the beacon

    [Fact]
    public void AMachineDoesNotAnswerItsOwnBeacon()
    {
        Guid mine = Guid.NewGuid();
        var self = new Beacon(mine, "Mum's laptop", "1.0.0", 1, 5000, "aa");

        Assert.False(self.IsWorthAnswering(mine));
        Assert.True(self.IsWorthAnswering(Guid.NewGuid()));
    }

    [Fact]
    public void ABeaconWithNoUsablePortIsIgnored()
    {
        // Anything at all can arrive on a multicast group, including other
        // software's packets and somebody's idea of a joke.
        var nonsense = new Beacon(Guid.NewGuid(), "x", "1.0.0", 1, 0, "aa");

        Assert.False(nonsense.IsWorthAnswering(Guid.NewGuid()));
    }

    [Fact]
    public async Task APublicNetworkIsReportedRatherThanShownAsAnEmptyList()
    {
        // Discovery finds nothing and no error is raised anywhere, so the only
        // way to tell this apart from "nobody else is running the app" is to ask
        // Windows - and to say so.
        var discovery = new UdpPeerDiscovery(new PublicNetwork());

        Assert.Equal(DiscoveryProblem.PublicNetwork, await discovery.ReadinessAsync());
    }

    [Fact]
    public async Task APrivateNetworkReportsNoProblem()
    {
        var discovery = new UdpPeerDiscovery(new PrivateNetwork());

        Assert.Equal(DiscoveryProblem.None, await discovery.ReadinessAsync());
    }

    // ------------------------------------------------------------------ setup

    private const int SharingVersionSchema = 1;

    private PeerCertificate Certificate(string machine)
    {
        string folder = Path.Combine(_root, machine);
        Directory.CreateDirectory(folder);

        var working = new WorkingFolder(folder);
        working.EnsureCreated();

        return new PeerCertificate(working);
    }

    private (PeerListener Listener, PeerCertificate Certificate) Listen(
        string machine, DecisionSet mine, HashSet<string>? paired = null)
    {
        PeerCertificate certificate = Certificate(machine);

        var listener = new PeerListener(
            certificate,
            () => Task.FromResult(mine),
            () => mine.Machine.Id,
            fingerprint => paired?.Contains(fingerprint) == true);

        listener.Start();
        _listening.Add(listener);

        return (listener, certificate);
    }

    private static DecisionSet Set(string name) =>
        DecisionSet.Empty(
            new MachineIdentity(Guid.NewGuid(), name, "1.0.0", SharingVersionSchema),
            new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc)) with
        {
            People =
            [
                new SharedPerson(
                    Guid.NewGuid(),
                    "Ana",
                    null,
                    new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
                    null),
            ],
        };

    private static MachineIdentity Me(string name) =>
        new(Guid.NewGuid(), name, "1.0.0", SharingVersionSchema);

    public async ValueTask DisposeAsync()
    {
        foreach (PeerListener listener in _listening)
        {
            await listener.DisposeAsync();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class PublicNetwork : INetworkProfile
    {
        public bool IsPublic() => true;
    }

    private sealed class PrivateNetwork : INetworkProfile
    {
        public bool IsPublic() => false;
    }
}

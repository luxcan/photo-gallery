using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Two machines reaching one folder two ways, and the mistake that looks the
/// same and is not.
/// </summary>
/// <remarks>
/// Pure, like the merge. Every rule here is about what two roots mean, which is
/// worth arguing out against tests rather than against two real network shares.
/// </remarks>
public sealed class SourcePairingTests
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Hers = new("11111111-0000-4000-8000-000000000001");
    private static readonly Guid His = new("22222222-0000-4000-8000-000000000002");
    private static readonly Guid Theirs = new("33333333-0000-4000-8000-000000000003");

    [Fact]
    public void AUncPathAndAMappedDriveLetterAreProposedAsOneFolder()
    {
        // Windows offers to map that drive letter for you, so comparing the text
        // would lock a family member out for doing an entirely normal thing.
        IReadOnlyList<PairingProposal> proposals = SourcePairing.Propose(
            [new SharedSource(Hers, @"\\192.168.50.103\PhotoGallery", 15823)],
            [new SharedSource(His, @"Z:\PhotoGallery", 15823)],
            "Dad's laptop",
            []);

        PairingProposal only = Assert.Single(proposals);
        Assert.Equal(PairingLikeness.SameName, only.Likeness);
        Assert.True(only.CanPair);
        Assert.Equal("Dad's laptop", only.MachineName);
    }

    [Fact]
    public void TheSameTextIsTheSurestPairAndIsOfferedFirst()
    {
        IReadOnlyList<PairingProposal> proposals = SourcePairing.Propose(
            [new SharedSource(Hers, @"\\192.168.50.103\PhotoGallery", 15823)],
            [
                new SharedSource(Theirs, @"D:\PhotoGallery", 12),
                new SharedSource(His, @"\\192.168.50.103\PhotoGallery", 15823),
            ],
            "Dad's laptop",
            []);

        Assert.Equal(2, proposals.Count);
        Assert.Equal(PairingLikeness.SamePath, proposals[0].Likeness);
        Assert.Equal(His, proposals[0].Theirs.SharedId);
    }

    [Fact]
    public void TwoMachinesFiledAtDifferentDepthsAreToldSoRatherThanPaired()
    {
        // The shape that must never be absorbed silently: every relative path
        // differs by a prefix, every match misses, and the exchange looks merely
        // empty.
        IReadOnlyList<PairingProposal> proposals = SourcePairing.Propose(
            [new SharedSource(Hers, @"\\192.168.50.103\PhotoGallery", 15823)],
            [new SharedSource(His, @"\\192.168.50.103\PhotoGallery\Photos", 15823)],
            "Dad's laptop",
            []);

        PairingProposal only = Assert.Single(proposals);
        Assert.Equal(PairingLikeness.FiledDifferently, only.Likeness);

        // Pairing them would not help - the paths below would still differ.
        Assert.False(only.CanPair);
    }

    [Fact]
    public void TwoUnrelatedFoldersAreNotProposedAtAll()
    {
        IReadOnlyList<PairingProposal> proposals = SourcePairing.Propose(
            [new SharedSource(Hers, @"\\192.168.50.103\PhotoGallery", 15823)],
            [new SharedSource(His, @"C:\Users\dad\Phone Dump", 402)],
            "Dad's laptop",
            []);

        Assert.Empty(proposals);
    }

    [Fact]
    public void AFolderAlreadyPairedIsNotProposedAgain()
    {
        IReadOnlyList<PairingProposal> proposals = SourcePairing.Propose(
            [new SharedSource(Hers, @"\\192.168.50.103\PhotoGallery", 15823)],
            [new SharedSource(His, @"Z:\PhotoGallery", 15823)],
            "Dad's laptop",
            [new SourceLink(Hers, His, Monday, Guid.NewGuid())]);

        Assert.Empty(proposals);
    }

    [Fact]
    public void TwoSourcesThatAlreadyShareAnIdAreNotProposed()
    {
        IReadOnlyList<PairingProposal> proposals = SourcePairing.Propose(
            [new SharedSource(Hers, @"\\192.168.50.103\PhotoGallery", 15823)],
            [new SharedSource(Hers, @"Z:\PhotoGallery", 15823)],
            "Dad's laptop",
            []);

        Assert.Empty(proposals);
    }

    [Fact]
    public void TheLowerIdWinsWhicheverMachineConfirmed()
    {
        // Two people can confirm the same pair at the same moment on two
        // laptops. Any rule that depends on who asked first ends with the two of
        // them swapping ids and never settling.
        var link = new SourceLink(His, Hers, Monday, Guid.NewGuid());

        Assert.Equal(Hers, link.Canonical);
        Assert.Equal(His, link.Absorbed);
        Assert.Equal(link.Ordered(), new SourceLink(His, Hers, Monday, Guid.NewGuid()).Ordered()
            with { DecidedBy = link.DecidedBy });
    }

    [Fact]
    public void APairedSourceIsRenamedToTheLowerId()
    {
        IReadOnlyDictionary<Guid, Guid> renames = SourcePairing.Adopt(
            [His], [new SourceLink(Hers, His, Monday, Guid.NewGuid())]);

        Assert.Equal(Hers, renames[His]);
    }

    [Fact]
    public void TheSourceThatAlreadyHoldsTheLowerIdIsNotRenamed()
    {
        IReadOnlyDictionary<Guid, Guid> renames = SourcePairing.Adopt(
            [Hers], [new SourceLink(Hers, His, Monday, Guid.NewGuid())]);

        Assert.Empty(renames);
    }

    [Fact]
    public void ThreeMachinesPairedPairwiseAllLandOnOneIdentity()
    {
        // A pairs with B, B pairs with C, and nobody ever confirmed A to C.
        // Following the chain is what stops the third machine being left out.
        SourceLink[] links =
        [
            new(Hers, His, Monday, Guid.NewGuid()),
            new(His, Theirs, Monday, Guid.NewGuid()),
        ];

        Assert.Equal(Hers, SourcePairing.Adopt([Theirs], links)[Theirs]);
        Assert.Equal(Hers, SourcePairing.Adopt([His], links)[His]);
        Assert.Empty(SourcePairing.Adopt([Hers], links));
    }

    [Fact]
    public void AnUnpairedSourceIsLeftAlone()
    {
        IReadOnlyDictionary<Guid, Guid> renames = SourcePairing.Adopt(
            [Theirs], [new SourceLink(Hers, His, Monday, Guid.NewGuid())]);

        Assert.Empty(renames);
    }
}

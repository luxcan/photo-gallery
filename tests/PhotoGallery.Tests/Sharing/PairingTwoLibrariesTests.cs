using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Two real libraries reaching one folder by different routes, paired, and every
/// decision matching afterwards.
/// </summary>
/// <remarks>
/// The pure rules are argued out in <see cref="SourcePairingTests"/>. What is
/// left to doubt is the part those cannot touch: that confirming a pair actually
/// rewrites the rows, that the confirmation travels, and that answers parked
/// under the old identity come with it.
/// </remarks>
public sealed class PairingTwoLibrariesTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task UnpairedMachinesShareNothingAndSayWhyRatherThanClaimingSuccess()
    {
        // The state before anybody pairs: the same pictures, reached two ways,
        // under two identities that have never been matched.
        Apart();
        NameAFaceOnMum();

        await Mum.Publishing.HandleAsync();
        MergeResult merged = await Dad.Merging.HandleAsync();

        Assert.Empty(Dad.Db.FaceAssignments);
        Assert.Contains(
            merged.Outcome.Refused,
            refused => refused.Reason == RefusalReason.NoSourceInCommon);

        // And the answer is offered rather than left to be worked out.
        PairingProposal offer = Assert.Single(merged.Pairings);
        Assert.Equal(PairingLikeness.SameName, offer.Likeness);
        Assert.True(offer.CanPair);
    }

    [Fact]
    public async Task OnceThePairIsConfirmedEveryDecisionMatches()
    {
        Apart();
        NameAFaceOnMum();

        await Mum.Publishing.HandleAsync();
        MergeResult first = await Dad.Merging.HandleAsync();

        PairingProposal offer = Assert.Single(first.Pairings);
        await Dad.Pairing.HandleAsync(offer.Mine.SharedId, offer.Theirs.SharedId);

        MergeResult second = await Dad.Merging.HandleAsync();

        Assert.Equal(1, second.Outcome.NamesGained);
        Assert.Single(Dad.Db.FaceAssignments);
    }

    [Fact]
    public async Task BothMachinesLandOnTheSameIdentityWhicheverConfirmed()
    {
        // Two people can confirm the same pair at the same moment on two
        // laptops. Any rule depending on who asked first ends with the two of
        // them swapping ids and never settling.
        Apart();

        Guid hers = Mum.CurrentSharedId;
        Guid his = Dad.CurrentSharedId;
        Guid lower = hers.CompareTo(his) <= 0 ? hers : his;

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        await Dad.Pairing.HandleAsync(his, hers);

        Assert.Equal(lower, Dad.CurrentSharedId);

        // Mum never confirmed anything; she takes the link from Dad's file.
        await Dad.Publishing.HandleAsync();
        await Mum.Merging.HandleAsync();

        Assert.Equal(lower, Mum.CurrentSharedId);
        Assert.Equal(Dad.CurrentSharedId, Mum.CurrentSharedId);
    }

    [Fact]
    public async Task AnswersParkedUnderTheOldIdentityComeWithTheRename()
    {
        // A held answer is keyed on the source. Left behind, it would wait for a
        // folder that no longer exists under that name - which is an evening's
        // work quietly stranded.
        Apart();

        // Mum names a face in a photograph Dad has not indexed, so his merge
        // parks it rather than applying it.
        Mum.Answer(
            Mum.Face(Mum.Photo(@"2026 Phone Dump\b.jpg"), Head),
            Mum.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

        await Mum.Publishing.HandleAsync();

        // Paired first, so the answer is held under the pre-rename identity.
        Guid his = Dad.CurrentSharedId;
        await Dad.Merging.HandleAsync();
        await Dad.Pairing.HandleAsync(his, Mum.CurrentSharedId);
        await Dad.Merging.HandleAsync();

        HeldDecision waiting = Assert.Single(Dad.Db.HeldDecisions);
        Assert.Equal(Dad.CurrentSharedId, waiting.SharedSourceId);

        // And it lands once the photograph turns up.
        Dad.Face(Dad.Photo(@"2026 Phone Dump\b.jpg"), Head);
        HeldResult swept = await Dad.Waiting.HandleAsync();

        Assert.Equal(1, swept.Applied);
        Assert.Single(Dad.Db.FaceAssignments);
    }

    [Fact]
    public async Task TwoMachinesFiledAtDifferentDepthsAreToldSoRatherThanShownAnEmptySuccess()
    {
        // Pairing would not help: every path below the two roots still differs
        // by a prefix. So the exchange says what is wrong instead of reporting
        // that it matched nothing and succeeded.
        Mum.Reaches(@"\\192.168.50.103\PhotoGallery");
        Dad.Reaches(@"\\192.168.50.103\PhotoGallery\Photos");

        NameAFaceOnMum();
        await Mum.Publishing.HandleAsync();

        MergeResult merged = await Dad.Merging.HandleAsync();

        PairingProposal filed = Assert.Single(merged.Pairings);
        Assert.Equal(PairingLikeness.FiledDifferently, filed.Likeness);
        Assert.False(filed.CanPair);

        Assert.Contains("file them differently", merged.Summary);
        Assert.Contains("no photo lines up", merged.Summary);
    }

    [Fact]
    public async Task AConfirmedPairIsNotOfferedAgain()
    {
        Apart();
        await Mum.Publishing.HandleAsync();
        MergeResult first = await Dad.Merging.HandleAsync();

        PairingProposal offer = Assert.Single(first.Pairings);
        await Dad.Pairing.HandleAsync(offer.Mine.SharedId, offer.Theirs.SharedId);

        MergeResult second = await Dad.Merging.HandleAsync();

        Assert.Empty(second.Pairings);
    }

    [Fact]
    public async Task PairingTwiceChangesNothingTheSecondTime()
    {
        Apart();
        Guid his = Dad.CurrentSharedId;
        Guid hers = Mum.CurrentSharedId;

        await Dad.Pairing.HandleAsync(his, hers);
        Guid after = Dad.CurrentSharedId;

        await Dad.Pairing.HandleAsync(his, hers);

        Assert.Equal(after, Dad.CurrentSharedId);
        Assert.Single(Dad.Db.PairedSources);
    }

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// The same photographs, reached two ways, under identities nobody has
    /// matched.
    /// </summary>
    private void Apart()
    {
        Mum.Reaches(@"\\192.168.50.103\PhotoGallery");
        Dad.Reaches(@"Z:\PhotoGallery");
    }

    private void NameAFaceOnMum()
    {
        Mum.Answer(
            Mum.Face(Mum.Photo(@"2019\a.jpg"), Head),
            Mum.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

        Dad.Face(Dad.Photo(@"2019\a.jpg"), Head);
    }

    public void Dispose() => _house.Dispose();
}

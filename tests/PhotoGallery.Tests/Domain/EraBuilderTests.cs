using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Tests.Domain;

public sealed class EraBuilderTests
{
    [Fact]
    public void Derive_OfAFaceThatNeverChangesIsOneEra()
    {
        // A grandparent across a decade. Cutting on the calendar would give ten
        // eras of the same face.
        IReadOnlyList<PersonEra> eras = EraBuilder.Derive(
            [.. Enumerable.Range(0, 12).Select(i => Face(i + 1, i * 300, 1))]);

        Assert.Single(eras);
        Assert.Equal(12, eras[0].SampleCount);
    }

    [Fact]
    public void Derive_CutsWhereTheFaceActuallyChanges()
    {
        FaceSample[] confirmed = [.. Run(1, fromDay: 0, fromAngle: 0), .. Run(100, 600, 80)];

        IReadOnlyList<PersonEra> eras = EraBuilder.Derive(confirmed);

        Assert.Equal(2, eras.Count);
        Assert.Equal(15, eras[0].SampleCount);
        Assert.Equal(15, eras[1].SampleCount);
        Assert.True(eras[0].ToUtc <= eras[1].FromUtc);
    }

    [Fact]
    public void Derive_IgnoresAHandfulOfOddPhotographsInTheMiddle()
    {
        // The failure this smoothing exists for. One bad angle, a hand in the
        // way, a dark room - none of that is anyone changing, and cutting on a
        // single face produced 21 eras from eighteen months of one baby, several
        // of them a single afternoon.
        FaceSample[] confirmed =
        [
            .. Run(1, fromDay: 0, fromAngle: 0),
            Face(50, 200, 95),
            Face(51, 205, 95),
            .. Run(60, fromDay: 220, fromAngle: 4),
        ];

        IReadOnlyList<PersonEra> eras = EraBuilder.Derive(confirmed);

        Assert.Single(eras);
    }

    [Fact]
    public void Derive_FoldsARunTooSmallToStandOnItsOwn()
    {
        // One odd photograph is a trick of the light, not a stage of someone's
        // life, and an era resting on it would match badly ever after.
        FaceSample[] confirmed =
        [
            .. Enumerable.Range(0, 6).Select(i => Face(i + 1, i * 20, i)),
            Face(7, 200, 85),
            .. Enumerable.Range(0, 5).Select(i => Face(i + 8, 260 + (i * 20), i + 2)),
        ];

        IReadOnlyList<PersonEra> eras = EraBuilder.Derive(confirmed);

        Assert.All(eras, era => Assert.True(era.SampleCount >= EraBuilder.MinimumSamples));
    }

    [Fact]
    public void Derive_ProducesErasThatCoverTheDatesTheyCameFrom()
    {
        FaceSample[] confirmed =
            [.. Enumerable.Range(0, 8).Select(i => Face(i + 1, i * 40, i))];

        IReadOnlyList<PersonEra> eras = EraBuilder.Derive(confirmed);

        Assert.True(eras[0].Covers(confirmed[0].TakenUtc));
        Assert.True(eras[^1].Covers(confirmed[^1].TakenUtc));
    }

    [Fact]
    public void Derive_GivesEachEraTheAverageOfItsOwnFaces()
    {
        FaceSample[] confirmed =
            [.. Enumerable.Range(0, 5).Select(i => Face(i + 1, i * 10, 10))];

        IReadOnlyList<PersonEra> eras = EraBuilder.Derive(confirmed);

        Assert.Equal(1f, eras[0].Centroid.SimilarityTo(TestEmbeddings.At(10)), 3);
    }

    [Fact]
    public void Derive_OfNoConfirmedFacesIsNoEras()
    {
        Assert.Empty(EraBuilder.Derive([]));
    }

    [Fact]
    public void EraFor_PicksTheOneCoveringThePhotographsOwnDate()
    {
        var person = new Person { DisplayName = "Test" };
        person.Eras.AddRange(EraBuilder.Derive([.. Run(1, 0, 0), .. Run(100, 600, 80)]));

        PersonEra? early = person.EraFor(Face(0, 60, 0).TakenUtc);
        PersonEra? late = person.EraFor(Face(0, 700, 0).TakenUtc);

        Assert.NotNull(early);
        Assert.NotNull(late);
        Assert.NotEqual(early.FromUtc, late.FromUtc);

        // A photograph from before anyone was photographed still gets the
        // earliest appearance rather than nothing at all.
        Assert.NotNull(person.EraFor(Face(0, -500, 0).TakenUtc));
    }

    private static readonly DateTime s_start = new(2014, 3, 11, 0, 0, 0, DateTimeKind.Utc);

    private static FaceSample Face(int id, int dayOffset, double angle) =>
        new(id, s_start.AddDays(dayOffset), TestEmbeddings.At(angle));

    /// <summary>
    /// Fifteen faces over five months, drifting slowly - enough to stand as an
    /// era on both counts the builder cares about.
    /// </summary>
    private static FaceSample[] Run(int firstId, int fromDay, double fromAngle) =>
        [.. Enumerable.Range(0, 15)
            .Select(i => Face(firstId + i, fromDay + (i * 12), fromAngle + i))];
}

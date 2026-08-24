using PhotoGallery.Domain.People;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// How old somebody was, from a year of birth and the date on a picture.
/// </summary>
/// <remarks>
/// A year is all the screen asks for, so every age here is the age reached
/// during that calendar year rather than the age on the day. The tests below
/// pin the two places that distinction is visible: the year of birth itself,
/// and any date claiming to fall before it.
/// </remarks>
public sealed class PersonAgeTests
{
    private static readonly DateTime InTwentyTwenty = new(2020, 7, 14, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Age_IsTheDifferenceBetweenTheYears() =>
        Assert.Equal(37, PersonAge.At(1983, InTwentyTwenty));

    [Fact]
    public void Age_WithoutAYearOfBirthIsNotKnown() =>
        Assert.Null(PersonAge.At(null, InTwentyTwenty));

    [Fact]
    public void Age_InTheYearOfBirthIsWrittenUnderOne()
    {
        // The one heading that cannot be a year out: within the calendar year of
        // their birth a person is necessarily under one, whichever side of the
        // birthday the photograph falls. It is also where being wrong would show
        // most, because a baby's months are the whole point of the screen.
        Assert.Equal(0, PersonAge.At(2015, new DateTime(2015, 12, 31, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("Under 1", PersonAge.Heading(0));
        Assert.DoesNotContain("0", PersonAge.Heading(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Age_BeforeTheYearOfBirthIsNegativeRatherThanClamped()
    {
        // Kept negative so the caller can set these aside together. Clamping to
        // nought would file a misdated picture under "Under 1", which is the one
        // group where a stranger's photograph would be least noticed.
        Assert.Equal(-3, PersonAge.At(2015, new DateTime(2012, 3, 14, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Age_BeforeTheBirthAllShareOneBucket()
    {
        // Otherwise every impossible year becomes its own heading, and a handful
        // of misdated files turn into a column of one-picture groups.
        Assert.Equal(PersonAge.BeforeBirth, PersonAge.Bucket(-3));
        Assert.Equal(PersonAge.BeforeBirth, PersonAge.Bucket(-11));
        Assert.Equal(5, PersonAge.Bucket(5));
        Assert.Equal(0, PersonAge.Bucket(0));
    }

    [Fact]
    public void Heading_ReadsAsTheUserWroteIt() =>
        Assert.Equal("Age 37", PersonAge.Heading(37));

    [Fact]
    public void Year_OutsideLivingMemoryOrInTheFutureIsRefused()
    {
        // Guards a typed "215" or "20155", not a claim about how long people live.
        DateTime today = new(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(PersonAge.IsPlausible(1983, today));
        Assert.True(PersonAge.IsPlausible(2026, today));
        Assert.False(PersonAge.IsPlausible(215, today));
        Assert.False(PersonAge.IsPlausible(2027, today));
    }
}

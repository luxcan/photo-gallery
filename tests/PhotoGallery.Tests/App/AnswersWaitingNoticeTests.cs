using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Sharing;
using PhotoGallery.Infrastructure.Storage;
using PhotoGallery.Tests.Sharing;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The sentence that floats over the app when another computer has answers this
/// one has not taken, in every arrangement of computers it can be about.
/// </summary>
/// <remarks>
/// A computer this library has never taken from cannot be named, because its
/// name is inside a file the look deliberately does not open - and it used to be
/// described in the place of a name. So the sentence began "a computer this
/// library has not taken answers from yet has answers this computer has not
/// taken yet.", lower case and twice over; two such computers were folded into
/// one; and beside a named one the description sorted first.
///
/// <para>Asked the way the app asks: the view model's look, over the real
/// handler, against real libraries and a real sync folder.</para>
/// </remarks>
public sealed class AnswersWaitingNoticeTests : IDisposable
{
    private readonly TwoLibraries _house = new TwoLibraries().Sharing();
    private readonly ServiceProvider _services;
    private readonly SharingViewModel _sharing;

    public AnswersWaitingNoticeTests()
    {
        // Dad's laptop is the one looking, as the app looks: in a scope, through
        // the handler it resolves there.
        _services = new ServiceCollection()
            .AddSingleton(Dad.Looking)
            .BuildServiceProvider();

        _sharing = new SharingViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileActivityLog(Dad.Working));
    }

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task OneComputerItHasTakenFromIsNamed()
    {
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        await PublishedAgainAsync(Mum);

        Assert.Equal(
            "Mum's laptop has answers this computer has not taken yet.",
            await NoticeAsync());
    }

    [Fact]
    public async Task OneComputerItHasNeverTakenFromIsAnotherComputer()
    {
        await Mum.Publishing.HandleAsync();

        Assert.Equal(
            "Another computer has answers this one has not taken yet.",
            await NoticeAsync());
    }

    [Fact]
    public async Task SeveralComputersItHasTakenFromAreAllNamed()
    {
        Library gran = _house.Add("Gran's laptop");
        Library ana = _house.Add("Ana's laptop");

        await Mum.Publishing.HandleAsync();
        await gran.Publishing.HandleAsync();
        await ana.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        await PublishedAgainAsync(Mum);
        await PublishedAgainAsync(gran);
        await PublishedAgainAsync(ana);

        Assert.Equal(
            "Ana's laptop, Gran's laptop and Mum's laptop have answers this computer has "
            + "not taken yet.",
            await NoticeAsync());
    }

    [Fact]
    public async Task ANamedComputerAndOneItHasNeverTakenFromAreOneSentence()
    {
        Library gran = _house.Add("Gran's laptop");

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();
        await PublishedAgainAsync(Mum);
        await gran.Publishing.HandleAsync();

        Assert.Equal(
            "Mum's laptop and another computer have answers this computer has not taken yet.",
            await NoticeAsync());
    }

    [Fact]
    public async Task SeveralComputersItHasNeverTakenFromAreNotFoldedIntoOne()
    {
        Library gran = _house.Add("Gran's laptop");

        await Mum.Publishing.HandleAsync();
        await gran.Publishing.HandleAsync();

        Assert.Equal(
            "Other computers have answers this one has not taken yet.",
            await NoticeAsync());
    }

    /// <summary>What the notice says, asked the way the app asks.</summary>
    private async Task<string?> NoticeAsync()
    {
        string? notice = await _sharing.LookForUpdatesAsync(TimeSpan.FromSeconds(30));
        Dad.Db.ChangeTracker.Clear();
        return notice;
    }

    /// <summary>
    /// A computer publishing again after this one last took from it.
    /// </summary>
    /// <remarks>
    /// Stamped rather than trusted, as the look's own tests do: publishing,
    /// taking and publishing again takes a millisecond here, and a file written
    /// twice inside one tick of the clock can still report the first write.
    /// </remarks>
    private async Task PublishedAgainAsync(Library library)
    {
        await library.Publishing.HandleAsync();

        File.SetLastWriteTimeUtc(
            Path.Combine(_house.SharedFolder, "answers", $"{library.MachineId:D}.json.gz"),
            DateTime.UtcNow.AddMinutes(1));
    }

    public void Dispose()
    {
        _services.Dispose();
        _house.Dispose();
    }
}

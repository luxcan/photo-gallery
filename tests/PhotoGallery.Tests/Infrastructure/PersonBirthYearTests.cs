using Microsoft.EntityFrameworkCore;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Storing the year somebody was born.
/// </summary>
/// <remarks>
/// These run the migrations against a real file rather than an in-memory model,
/// which is the only way the column's absence would show: a migration whose
/// designer file never reached the commit compiles, passes every test built on
/// the model, and then quietly does nothing to the user's library.
/// </remarks>
public sealed class PersonBirthYearTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqlitePeopleRepository _repository;
    private readonly SqlitePeopleReader _reader;

    public PersonBirthYearTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-born-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();

        _repository = new SqlitePeopleRepository(_db);
        _reader = new SqlitePeopleReader(_db);
    }

    [Fact]
    public async Task BirthYear_IsRememberedAcrossAReadBack()
    {
        int id = await _repository.EnsurePersonAsync("Ana Lim");

        await _repository.SetBirthYearAsync(id, 2015);

        Assert.Equal(2015, await BirthYearOf(id));
    }

    [Fact]
    public async Task BirthYear_StartsUnknownForSomebodyJustNamed()
    {
        // Optional, and most people in a family library never get one. Nothing
        // may be inferred from the pictures on their behalf.
        int id = await _repository.EnsurePersonAsync("Noor");

        Assert.Null(await BirthYearOf(id));
    }

    [Fact]
    public async Task BirthYear_CanBeClearedBackToUnknown()
    {
        int id = await _repository.EnsurePersonAsync("Vera");
        await _repository.SetBirthYearAsync(id, 1988);

        await _repository.SetBirthYearAsync(id, null);

        Assert.Null(await BirthYearOf(id));
    }

    [Fact]
    public async Task BirthYear_LeavesTheNameAlone()
    {
        // The rename path updates one column by name; so does this. Writing the
        // whole entity back would let either overwrite the other.
        int id = await _repository.EnsurePersonAsync("Ana Reyes");

        await _repository.SetBirthYearAsync(id, 2018);

        Person person = await Reload(id);
        Assert.Equal("Ana Reyes", person.DisplayName);
        Assert.Equal(2018, person.BirthYear);
    }

    [Theory]
    [InlineData(215)]
    [InlineData(20155)]
    [InlineData(1899)]
    public async Task BirthYear_ThatCouldOnlyBeASlipIsRefused(int year)
    {
        int id = await _repository.EnsurePersonAsync("Elsa");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _repository.SetBirthYearAsync(id, year));

        Assert.Null(await BirthYearOf(id));
    }

    [Fact]
    public async Task BirthYear_InTheFutureIsRefused()
    {
        int id = await _repository.EnsurePersonAsync("Ivy");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _repository.SetBirthYearAsync(id, DateTime.Today.Year + 1));
    }

    private async Task<int?> BirthYearOf(int id) => (await Reload(id)).BirthYear;

    private async Task<Person> Reload(int id)
    {
        _db.ChangeTracker.Clear();
        IReadOnlyList<Person> people = await _reader.GetPeopleAsync();
        return people.Single(person => person.Id == id);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A handle the provider has not let go of yet. The temp folder is
            // the operating system's problem after that.
        }
    }
}

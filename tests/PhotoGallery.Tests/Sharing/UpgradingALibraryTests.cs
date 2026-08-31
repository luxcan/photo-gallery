using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// What happens to a library that already exists when sharing arrives.
/// </summary>
/// <remarks>
/// The riskiest thing in this feature, and the one no amount of testing against
/// a fresh database would catch. Three of the new columns are unique and every
/// row that already exists takes the column's default, which is one value: a
/// library with two people would refuse to migrate at all, and one with a single
/// person of each kind would migrate into a state where every machine in the
/// house claims the same identity for a different person.
///
/// <para>So this runs the real upgrade: migrate to the release before sharing,
/// put rows in as that schema, and migrate forward. The rows go in with raw SQL
/// because the model has moved on and the entities carry columns that table does
/// not have yet - which is exactly the situation being tested.</para>
/// </remarks>
public sealed class UpgradingALibraryTests : IDisposable
{
    /// <summary>The last release before any of this existed.</summary>
    private const string BeforeSharing = "AddCollectionRules";

    private readonly string _root;
    private readonly GalleryDbContext _db;

    public UpgradingALibraryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);

        _db.GetService<IMigrator>().Migrate(BeforeSharing);
    }

    [Fact]
    public void EveryPersonComesOutWithAnIdentityOfTheirOwn()
    {
        Sql("INSERT INTO People (DisplayName) VALUES ('Ana'), ('Ben'), ('Mei');");

        Upgrade();

        List<Guid> minted = [.. _db.People.Select(p => p.PublicId)];

        Assert.Equal(3, minted.Count);
        Assert.DoesNotContain(Guid.Empty, minted);
        Assert.Equal(3, minted.Distinct().Count());
    }

    [Fact]
    public void SoDoesEveryAlbumAndEverySource()
    {
        Album("Genting", renamed: false);
        Album("March 2019", renamed: false);
        Sql($"INSERT INTO PhotoSources (Path, AddedUtc) VALUES ('{_root}', '2026-01-01 00:00:00');");
        Sql($"INSERT INTO PhotoSources (Path, AddedUtc) VALUES ('{_root}-two', '2026-01-01 00:00:00');");

        Upgrade();

        Assert.Equal(2, _db.Collections.Select(c => c.PublicId).Distinct().Count());
        Assert.Equal(2, _db.PhotoSources.Select(s => s.SharedId).Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, _db.PhotoSources.Select(s => s.SharedId));
    }

    [Fact]
    public void AnIdentityIsOneTheAppCanLookSomebodyUpBy()
    {
        // Reading it back is not the test. A Guid is parsed case-insensitively,
        // so a badly minted identity comes out of the database looking perfect
        // and then matches nothing: it reaches SQLite as an upper-case parameter
        // and SQLite compares text case-sensitively. Every person on an upgraded
        // library would be unfindable by the identity the rest of the house knows
        // them by, and nothing would look broken until two machines failed to
        // agree about anybody. So the assertion is a lookup, not a read.
        Sql("INSERT INTO People (DisplayName) VALUES ('Ana');");

        Upgrade();

        Guid minted = _db.People.Single().PublicId;

        Assert.Equal(4, minted.ToString()[14] - '0');
        Assert.Equal("Ana", _db.People.Single(p => p.PublicId == minted).DisplayName);
    }

    [Fact]
    public void AndOneWrittenTheWayTheAppItselfWritesThem()
    {
        // The same claim from the other side, so a change to either the minting
        // or the mapping is caught by something that says which.
        Sql("INSERT INTO People (DisplayName) VALUES ('Ana');");
        Upgrade();

        Guid minted = _db.People.Single().PublicId;

        Person ben = new() { PublicId = Guid.NewGuid(), DisplayName = "Ben" };
        _db.People.Add(ben);
        _db.SaveChanges();

        // Each against its own identity, upper-cased: the claim is that the two
        // writers agree on a form, not that two people share one.
        Assert.Equal(minted.ToString().ToUpperInvariant(), StoredForm("Ana"));
        Assert.Equal(ben.PublicId.ToString().ToUpperInvariant(), StoredForm("Ben"));
    }

    [Fact]
    public void ANameSomebodyTypedIsStillANameNoPassMayWriteOver()
    {
        // The flag being dropped without its meaning being carried across is the
        // quiet way this migration could cost somebody every album name they
        // have ever typed.
        Album("Genting Trip", renamed: true);
        Album("March 2019", renamed: false);

        Upgrade();

        Assert.NotNull(_db.Collections.Single(c => c.Name == "Genting Trip").NamedUtc);
        Assert.Null(_db.Collections.Single(c => c.Name == "March 2019").NamedUtc);
    }

    [Fact]
    public void ANameNobodyHasRetypedSinceLosesToOneSomebodyHas()
    {
        // Which is the answer wanted at every point where two machines differ:
        // the moment is genuinely unknown, so it must not beat a real one.
        Album("Genting Trip", renamed: true);

        Upgrade();

        Assert.Equal(DateTime.MinValue, _db.Collections.Single().NamedUtc);
    }

    [Fact]
    public void AnswersGivenBeforeThisReleaseSayTheyCannotSayWhen()
    {
        Sql("INSERT INTO People (DisplayName) VALUES ('Ana');");
        Sql($"INSERT INTO PhotoSources (Path, AddedUtc) VALUES ('{_root}', '2026-01-01 00:00:00');");
        Sql("""
            INSERT INTO Assets
                (PhotoSourceId, RelativePath, Length, ModifiedUtc, CreatedUtc, IndexedUtc,
                 Kind, Status, Rotation)
            VALUES (1, '2019\a.jpg', 1024, '2020-01-01 00:00:00', '2020-01-01 00:00:00',
                    '2020-01-01 00:00:00', 0, 3, 0);
            """);
        Sql("""
            INSERT INTO Faces (AssetId, BoundsX, BoundsY, BoundsWidth, BoundsHeight,
                               DetectScore, Embedding)
            VALUES (1, 10, 10, 40, 40, 0.99, x'00');
            """);
        Sql("INSERT INTO FaceAssignments (FaceId, PersonId, Source) VALUES (1, 1, 1);");

        Upgrade();

        // Honest rather than tidy: the decision happened, the moment was never
        // recorded, and the merge treats that as losing to any real date.
        Assert.Equal(DateTime.MinValue, _db.FaceAssignments.Single().DecidedUtc);
    }

    [Fact]
    public async Task TheMachineNamesItselfOnFirstUseRatherThanInTheMigration()
    {
        // Nothing in a migration knows what this computer is called, and one that
        // guessed would bake the name of whoever generated the SQL into every
        // library it touched. The row is left empty and the index fills it in.
        Sql("INSERT INTO LibrarySettings (Id, Theme, GalleryCellSize, GallerySortOrder, "
          + "NavigationCollapsed) VALUES (1, 0, 200, 0, 0);");

        Upgrade();

        Assert.Equal(Guid.Empty, _db.LibrarySettings.Single().MachineId);

        LibrarySettings settings = await new SqliteLibraryIndex(_db).GetSettingsAsync();

        Assert.NotEqual(Guid.Empty, settings.MachineId);
        Assert.NotEmpty(settings.MachineName);
    }

    [Fact]
    public void AnEmptyLibraryUpgradesToo()
    {
        Upgrade();

        Assert.Empty(_db.People);
        Assert.Empty(_db.HeldDecisions);
        Assert.Empty(_db.KnownMachines);
    }

    private void Upgrade()
    {
        _db.Database.Migrate();
        _db.ChangeTracker.Clear();
    }

    private void Album(string name, bool renamed) =>
        Sql($"""
             INSERT INTO Collections
                 (Name, StartUtc, EndUtc, CoverAssetId, Kind, Origin, WasRenamed, BuiltUtc)
             VALUES ('{name}', '2019-03-03 00:00:00', '2019-03-05 00:00:00', 0,
                     {(int)CollectionKind.Event}, {(int)CollectionOrigin.Made},
                     {(renamed ? 1 : 0)}, '2019-03-06 00:00:00');
             """);

    private void Sql(string sql) => _db.Database.ExecuteSqlRaw(sql);

    /// <summary>The text actually in the column, rather than what parsing it gives.</summary>
    private string StoredForm(string displayName) =>
        _db.Database
            .SqlQueryRaw<string>(
                "SELECT PublicId AS Value FROM People WHERE DisplayName = {0}", displayName)
            .Single();

    public void Dispose()
    {
        _db.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A handle the runtime has not finished with. The folder is under the
            // temp root; failing a passing test over it would cost more.
        }
    }
}

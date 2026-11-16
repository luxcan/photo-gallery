using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// People deleted before a deletion blanked them lose their name and birth year
/// on the upgrade.
/// </summary>
/// <remarks>
/// The real upgrade, run on rows written as the schema before it: every one of
/// those tombstones was being copied into the sync folder, name and all, on
/// every sync - and the people still here must come through untouched.
/// </remarks>
public sealed class DeletedPeopleUpgradeTests
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheUpgradeBlanksTheDeletedAndLeavesEverybodyElse()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pg-forget-upgrade-{Guid.NewGuid():N}.db");

        try
        {
            using var db = new GalleryDbContext(
                new DbContextOptionsBuilder<GalleryDbContext>()
                    .UseSqlite($"Data Source={path}")
                    .Options);

            IMigrator migrator = db.GetService<IMigrator>();
            string before = db.Database.GetMigrations()
                .TakeWhile(name => !name.EndsWith("_ForgetDeletedPeople", StringComparison.Ordinal))
                .Last();
            migrator.Migrate(before);

            db.People.AddRange(
                new Person { DisplayName = "Ana", BirthYear = 2015 },
                new Person { DisplayName = "Ben", BirthYear = 1980, DeletedUtc = Monday },
                new Person { DisplayName = "Mei", DeletedUtc = Monday });
            db.SaveChanges();
            db.ChangeTracker.Clear();

            migrator.Migrate();

            Person ana = db.People.AsNoTracking().Single();
            Assert.Equal("Ana", ana.DisplayName);
            Assert.Equal(2015, ana.BirthYear);

            List<Person> deleted =
                [.. db.People.IgnoreQueryFilters().AsNoTracking().Where(p => p.DeletedUtc != null)];

            Assert.Equal(2, deleted.Count);
            Assert.All(deleted, person =>
            {
                Assert.Equal(string.Empty, person.DisplayName);
                Assert.Null(person.BirthYear);
                Assert.Equal(Monday, person.DeletedUtc);
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}

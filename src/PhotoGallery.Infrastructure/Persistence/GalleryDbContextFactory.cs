using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PhotoGallery.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time. The real connection string is
/// built at runtime from whichever working folder the user opened, which the
/// tooling has no way to know.
/// </summary>
public sealed class GalleryDbContextFactory : IDesignTimeDbContextFactory<GalleryDbContext>
{
    public GalleryDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite("Data Source=design-time.db")
                .Options;

        return new GalleryDbContext(options);
    }
}

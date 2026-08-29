using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class LibrarySettingsConfiguration : IEntityTypeConfiguration<LibrarySettings>
{
    public void Configure(EntityTypeBuilder<LibrarySettings> builder)
    {
        builder.ToTable("LibrarySettings");
        builder.HasKey(s => s.Id);

        // The Id is fixed rather than generated: there is exactly one library
        // per working folder, so the row is upserted, never appended to.
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Declared to the model, not left to the property initialiser. A C#
        // default applies to newly constructed objects; a library that already
        // has a settings row would take the column's own default instead, and
        // that is 0 - a grid of zero-sized cells on the first run after
        // upgrading.
        builder.Property(s => s.GalleryCellSize).HasDefaultValue(200d);

        // Declared even though NewestFirst is already zero, so the intent is on
        // the record rather than resting on the enum member happening to be the
        // one a new column defaults to.
        builder.Property(s => s.GallerySortOrder)
            .HasDefaultValue(GallerySortOrder.NewestFirst);

        // Declared for the same reason: a library that predates the column takes
        // the column's default, and the nav should open the way it always has.
        builder.Property(s => s.NavigationCollapsed).HasDefaultValue(false);

        builder.Property(s => s.MachineName).IsRequired().HasMaxLength(128);
        builder.Property(s => s.SharedFolder).HasMaxLength(512);
    }
}

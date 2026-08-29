using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class PhotoSourceConfiguration : IEntityTypeConfiguration<PhotoSource>
{
    public void Configure(EntityTypeBuilder<PhotoSource> builder)
    {
        builder.ToTable("PhotoSources");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Path).IsRequired().HasMaxLength(512);
        builder.HasIndex(s => s.Path).IsUnique();

        // Two sources in one library cannot be the same folder on another
        // machine, so pairing the second to a shared id already taken is a
        // mistake the database refuses rather than one a handler has to remember.
        builder.HasIndex(s => s.SharedId).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence.Converters;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.RelativePath).IsRequired().HasMaxLength(1024);

        // Two sources may legitimately hold the same relative path, so
        // uniqueness is per source, and removing a source removes its assets.
        builder.HasIndex(a => new { a.PhotoSourceId, a.RelativePath }).IsUnique();
        builder.HasOne<PhotoSource>()
            .WithMany()
            .HasForeignKey(a => a.PhotoSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Exact-duplicate detection groups by size first, so this index carries
        // the pre-filter that removed 97.5% of hashing work.
        builder.HasIndex(a => a.Length);
        builder.HasIndex(a => a.ContentHash);
        builder.HasIndex(a => a.TakenUtc);

        // The generating pass asks for pending rows and nothing else, so the one
        // query that runs before an hour of reading should not scan the table.
        builder.HasIndex(a => a.Status);

        // The same reasoning for the face pass, which asks for the photos it has
        // not looked at yet.
        builder.HasIndex(a => a.FacesDetectedUtc);

        // And for the locating pass, which asks the same shape of question.
        builder.HasIndex(a => a.LocationReadUtc);

        // Searching by place filters on this column directly, once per keystroke
        // that resolves to a place. The AddPlaces migration added the column
        // without one, which was fine while nothing read it.
        builder.HasIndex(a => a.PlaceId);

        builder.Property(a => a.ContentHash).HasMaxLength(64);
        builder.Property(a => a.ThumbnailName).HasMaxLength(128);

        builder.Property(a => a.PerceptualHash)
            .HasConversion(new PerceptualHashConverter())
            .HasMaxLength(16);

        // Derived from RelativePath; useful in code, not stored.
        builder.Ignore(a => a.TopFolder);
        builder.Ignore(a => a.Depth);
    }
}

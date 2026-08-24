using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Search;
using PhotoGallery.Infrastructure.Persistence.Converters;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class PhotoContentConfiguration : IEntityTypeConfiguration<PhotoContent>
{
    public void Configure(EntityTypeBuilder<PhotoContent> builder)
    {
        builder.ToTable("PhotoContent");

        // The asset is the key. One picture has one answer to what it is of, so
        // there is nothing a surrogate identity would distinguish.
        builder.HasKey(content => content.AssetId);

        builder.HasOne(content => content.Asset)
            .WithMany()
            .HasForeignKey(content => content.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(content => content.Vector)
            .HasConversion(new ContentEmbeddingConverter())
            .IsRequired();

        builder.Property(content => content.ThumbnailName)
            .IsRequired();

        // Two rows sharing a rendition share an answer, and the pass looks them
        // up that way rather than reading the same preview twice.
        builder.HasIndex(content => content.ThumbnailName);
    }
}

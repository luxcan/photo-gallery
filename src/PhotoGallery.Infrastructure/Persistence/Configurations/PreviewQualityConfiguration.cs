using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class PreviewQualityConfiguration : IEntityTypeConfiguration<PreviewQuality>
{
    public void Configure(EntityTypeBuilder<PreviewQuality> builder)
    {
        builder.ToTable("PreviewQuality");
        builder.HasKey(quality => quality.AssetId);

        builder.HasOne(quality => quality.Asset)
            .WithOne()
            .HasForeignKey<PreviewQuality>(quality => quality.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(quality => quality.ThumbnailName)
            .HasMaxLength(128)
            .IsRequired();
    }
}

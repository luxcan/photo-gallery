using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class VideoKeyframeConfiguration : IEntityTypeConfiguration<VideoKeyframe>
{
    public void Configure(EntityTypeBuilder<VideoKeyframe> builder)
    {
        builder.ToTable("VideoKeyframes");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.ThumbnailName).IsRequired().HasMaxLength(128);

        // One frame per ordinal per video, enforced rather than assumed: the
        // pass writes a video's frames again whenever its bytes change, and a
        // second row for the same ordinal would leave the face pass reading one
        // frame twice and calling the faces in it two different sets.
        builder.HasIndex(k => new { k.AssetId, k.Ordinal }).IsUnique();

        // The frames of one video are always wanted together - the face pass
        // reads them as a set - and a video losing its row must not leave its
        // frames behind naming nothing.
        builder.HasOne(k => k.Asset)
            .WithMany()
            .HasForeignKey(k => k.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

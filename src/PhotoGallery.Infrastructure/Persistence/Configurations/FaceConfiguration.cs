using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Persistence.Converters;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class FaceConfiguration : IEntityTypeConfiguration<Face>
{
    public void Configure(EntityTypeBuilder<Face> builder)
    {
        builder.ToTable("Faces");
        builder.HasKey(f => f.Id);

        builder.HasOne(f => f.Asset)
            .WithMany()
            .HasForeignKey(f => f.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.AssetId);

        builder.Property(f => f.Embedding)
            .HasConversion(new FaceEmbeddingConverter())
            .IsRequired();

        builder.ComplexProperty(f => f.Bounds, bounds =>
        {
            bounds.Property(b => b.X).HasColumnName("BoundsX");
            bounds.Property(b => b.Y).HasColumnName("BoundsY");
            bounds.Property(b => b.Width).HasColumnName("BoundsWidth");
            bounds.Property(b => b.Height).HasColumnName("BoundsHeight");
            bounds.Ignore(b => b.Area);
        });
    }
}

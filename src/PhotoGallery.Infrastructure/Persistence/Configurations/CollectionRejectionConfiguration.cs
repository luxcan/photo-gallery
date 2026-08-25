using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class CollectionRejectionConfiguration
    : IEntityTypeConfiguration<CollectionRejection>
{
    public void Configure(EntityTypeBuilder<CollectionRejection> builder)
    {
        builder.ToTable("CollectionRejections");

        // Keyed on the span rather than on the collection row, so the memory
        // outlives the rebuild that replaces that row.
        builder.HasKey(r => new { r.AssetId, r.ProposalKey });

        builder.Property(r => r.ProposalKey).HasMaxLength(24);

        builder.HasOne(r => r.Asset)
            .WithMany()
            .HasForeignKey(r => r.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        // The build reads every rejection for the spans it is about to offer.
        builder.HasIndex(r => r.ProposalKey);
    }
}

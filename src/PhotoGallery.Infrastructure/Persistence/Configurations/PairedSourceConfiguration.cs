using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class PairedSourceConfiguration : IEntityTypeConfiguration<PairedSource>
{
    public void Configure(EntityTypeBuilder<PairedSource> builder)
    {
        builder.ToTable("PairedSources");
        builder.HasKey(p => p.Id);

        // One row per pair, whichever way round it arrived. Every link is
        // ordered before it is stored, so the pair is the key.
        builder.HasIndex(p => new { p.Left, p.Right }).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class HeldDecisionConfiguration : IEntityTypeConfiguration<HeldDecision>
{
    public void Configure(EntityTypeBuilder<HeldDecision> builder)
    {
        builder.ToTable("HeldDecisions");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.RelativePath).IsRequired().HasMaxLength(1024);
        builder.Property(h => h.Part).IsRequired().HasMaxLength(128);
        builder.Property(h => h.Payload).IsRequired();

        builder.Ignore(h => h.Key);

        // One row per answer, which is what makes merging twice change nothing
        // the second time. Without it every merge would append a fresh copy of
        // every answer still waiting, and the table would grow with the number of
        // times somebody pressed the button rather than with what they decided.
        builder.HasIndex(h => new
        {
            h.SharedSourceId,
            h.RelativePath,
            h.Kind,
            h.Part,
        }).IsUnique();

        // How a scan finds what has been waiting for the photographs it has just
        // brought in: the key, in the shape the sweep asks for it.
        builder.HasIndex(h => new { h.SharedSourceId, h.RelativePath });

        // No foreign key to an asset, deliberately. The whole point of the table
        // is that the photograph is not here yet.
    }
}

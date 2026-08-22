using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class DuplicateMemberConfiguration : IEntityTypeConfiguration<DuplicateMember>
{
    public void Configure(EntityTypeBuilder<DuplicateMember> builder)
    {
        builder.ToTable("DuplicateMembers");
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.Asset)
            .WithMany()
            .HasForeignKey(m => m.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.DuplicateSetId, m.AssetId }).IsUnique();
    }
}

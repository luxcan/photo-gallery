using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class CollectionMemberConfiguration : IEntityTypeConfiguration<CollectionMember>
{
    public void Configure(EntityTypeBuilder<CollectionMember> builder)
    {
        builder.ToTable("CollectionMembers");

        // The asset is the key, and that one line is the rule that a photograph
        // belongs to at most one collection. Enforced here rather than in a
        // handler because there are three paths that write memberships, and the
        // database holds whichever of them forgets.
        builder.HasKey(m => m.AssetId);

        builder.HasOne(m => m.Asset)
            .WithMany()
            .HasForeignKey(m => m.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.CollectionId);
    }
}

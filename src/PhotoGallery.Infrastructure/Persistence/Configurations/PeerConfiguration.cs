using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class PeerConfiguration : IEntityTypeConfiguration<Peer>
{
    public void Configure(EntityTypeBuilder<Peer> builder)
    {
        builder.ToTable("Peers");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(128);
        builder.Property(p => p.Fingerprint).HasMaxLength(128);

        // A machine is one row however many times it is heard from, and however
        // many ways: the same laptop reached through the shared folder and later
        // over a direct connection must not become two entries on the screen.
        builder.HasIndex(p => p.MachineId).IsUnique();
    }
}

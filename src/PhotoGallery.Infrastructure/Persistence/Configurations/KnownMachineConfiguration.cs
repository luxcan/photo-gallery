using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class KnownMachineConfiguration : IEntityTypeConfiguration<KnownMachine>
{
    public void Configure(EntityTypeBuilder<KnownMachine> builder)
    {
        builder.ToTable("KnownMachines");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).IsRequired().HasMaxLength(128);

        // A machine is one row however many times it is heard from: the same
        // laptop merged from on Monday and again on Friday must not become two
        // entries on the screen.
        builder.HasIndex(m => m.MachineId).IsUnique();
    }
}

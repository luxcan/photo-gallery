using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class AlbumDismissalConfiguration : IEntityTypeConfiguration<AlbumDismissal>
{
    public void Configure(EntityTypeBuilder<AlbumDismissal> builder)
    {
        builder.ToTable("AlbumDismissals");

        // One row per run of days, holding the latest dismissal of it. Keyed on
        // the days rather than on an album row for the reason a refusal is:
        // the row it was about is gone, and a later suggestion for the same
        // days is a different row.
        builder.HasKey(d => d.ProposalKey);

        builder.Property(d => d.ProposalKey).HasMaxLength(24);

        // No foreign key and no tombstone filter. It points at no album - the
        // whole point is that the album is not there - and nothing about it is
        // ever deleted.
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class AlbumFileMoveConfiguration : IEntityTypeConfiguration<AlbumFileMove>
{
    public void Configure(EntityTypeBuilder<AlbumFileMove> builder)
    {
        builder.ToTable("AlbumFileMoves");
        builder.HasKey(move => move.Id);

        builder.Property(move => move.SourceRelativePath).IsRequired().HasMaxLength(1024);
        builder.Property(move => move.DestinationRelativePath).IsRequired().HasMaxLength(1024);
        builder.Property(move => move.Error).HasMaxLength(1024);

        builder.HasIndex(move => move.OperationId);
        builder.HasIndex(move => move.State);
        builder.HasIndex(move => move.AssetId);
    }
}

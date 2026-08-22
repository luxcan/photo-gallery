using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Persistence.Converters;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class PersonEraConfiguration : IEntityTypeConfiguration<PersonEra>
{
    public void Configure(EntityTypeBuilder<PersonEra> builder)
    {
        builder.ToTable("PersonEras");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Centroid)
            .HasConversion(new FaceEmbeddingConverter())
            .IsRequired();

        builder.HasIndex(e => new { e.PersonId, e.FromUtc });
    }
}

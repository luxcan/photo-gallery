using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Places;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> builder)
    {
        builder.ToTable("Places");

        builder.HasKey(place => place.Id);

        builder.Property(place => place.Name)
            .IsRequired();

        // The gazetteer's own identifier, unique so that resolving the same
        // coordinates twice cannot quietly insert a second row for one town.
        builder.HasIndex(place => place.GeoNameId)
            .IsUnique();
    }
}

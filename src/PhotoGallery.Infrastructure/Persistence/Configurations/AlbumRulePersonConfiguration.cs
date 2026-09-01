using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class AlbumRulePersonConfiguration
    : IEntityTypeConfiguration<AlbumRulePerson>
{
    public void Configure(EntityTypeBuilder<AlbumRulePerson> builder)
    {
        builder.ToTable("AlbumRulePeople");
        builder.HasKey(rule => new { rule.AlbumId, rule.PersonId });

        builder.HasOne(rule => rule.Person)
            .WithMany()
            .HasForeignKey(rule => rule.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Somebody removed from the library takes their half of every rule with
        // them; the rest of the rule stands.
        builder.HasIndex(rule => rule.PersonId);
    }
}

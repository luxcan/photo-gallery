using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class CollectionRulePersonConfiguration
    : IEntityTypeConfiguration<CollectionRulePerson>
{
    public void Configure(EntityTypeBuilder<CollectionRulePerson> builder)
    {
        builder.ToTable("CollectionRulePeople");
        builder.HasKey(rule => new { rule.CollectionId, rule.PersonId });

        builder.HasOne(rule => rule.Person)
            .WithMany()
            .HasForeignKey(rule => rule.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Somebody removed from the library takes their half of every rule with
        // them; the rest of the rule stands.
        builder.HasIndex(rule => rule.PersonId);
    }
}

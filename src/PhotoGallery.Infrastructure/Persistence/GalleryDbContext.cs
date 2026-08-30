using Microsoft.EntityFrameworkCore;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Places;
using PhotoGallery.Domain.Search;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Persistence;

/// <summary>The SQLite index that lives in the working folder.</summary>
public sealed class GalleryDbContext : DbContext
{
    public GalleryDbContext(DbContextOptions<GalleryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<Face> Faces => Set<Face>();

    public DbSet<VideoKeyframe> VideoKeyframes => Set<VideoKeyframe>();

    public DbSet<PhotoContent> PhotoContent => Set<PhotoContent>();

    public DbSet<Place> Places => Set<Place>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<PersonEra> PersonEras => Set<PersonEra>();

    public DbSet<FaceAssignment> FaceAssignments => Set<FaceAssignment>();

    public DbSet<DuplicateSet> DuplicateSets => Set<DuplicateSet>();

    public DbSet<DuplicateMember> DuplicateMembers => Set<DuplicateMember>();

    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<CollectionMember> CollectionMembers => Set<CollectionMember>();

    public DbSet<CollectionRejection> CollectionRejections => Set<CollectionRejection>();

    public DbSet<CollectionRulePerson> CollectionRulePeople => Set<CollectionRulePerson>();

    public DbSet<CollectionRulePlace> CollectionRulePlaces => Set<CollectionRulePlace>();

    public DbSet<LibrarySettings> LibrarySettings => Set<LibrarySettings>();

    public DbSet<PhotoSource> PhotoSources => Set<PhotoSource>();

    public DbSet<HeldDecision> HeldDecisions => Set<HeldDecision>();

    public DbSet<KnownMachine> KnownMachines => Set<KnownMachine>();

    public DbSet<PairedSource> PairedSources => Set<PairedSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GalleryDbContext).Assembly);
    }
}

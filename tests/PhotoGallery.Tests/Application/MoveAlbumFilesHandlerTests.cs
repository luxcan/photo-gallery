using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

public sealed class MoveAlbumFilesHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;
    private readonly string _destination;
    private readonly GalleryDbContext _db;
    private readonly SqliteAlbumFileMoveRepository _repository;
    private readonly MoveAlbumFilesHandler _handler;

    public MoveAlbumFilesHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-album-move-{Guid.NewGuid():N}");
        _source = Path.Combine(_root, "photos");
        _destination = Path.Combine(_source, "Together");
        Directory.CreateDirectory(_destination);

        var workingFolder = new WorkingFolder(Path.Combine(_root, "library"));
        workingFolder.EnsureCreated();

        _db = new GalleryDbContext(new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={workingFolder.DatabasePath};Pooling=False")
            .Options);
        _db.Database.Migrate();

        _db.PhotoSources.Add(new PhotoSource
        {
            Id = 1,
            Path = _source,
            AddedUtc = DateTime.UtcNow,
        });
        _db.SaveChanges();

        _repository = new SqliteAlbumFileMoveRepository(_db);
        _handler = new MoveAlbumFilesHandler(
            _repository, new WindowsOriginalFileMover(), workingFolder);
    }

    [Fact]
    public async Task Handle_MovesOriginalsAndChangesPathsWithoutChangingTheirIdentity()
    {
        int albumId = AddAlbum(AlbumOrigin.Accepted);
        int photo = AddFile(albumId, Path.Combine("Old", "same.jpg"), "first");
        int video = AddFile(albumId, Path.Combine("Elsewhere", "clip.mp4"), "second");

        // An unrelated file already owns this name. It must not be overwritten.
        string existing = Path.Combine(_destination, "same.jpg");
        await File.WriteAllTextAsync(existing, "keep me");

        AlbumMovePlan plan = await _handler.PlanAsync(albumId, _destination);

        Assert.Equal(2, plan.Moving);
        Assert.Equal(1, plan.Renamed);
        Assert.Contains(plan.Items, item =>
            item.AssetId == photo && item.DestinationRelativePath == @"Together\same (2).jpg");

        AlbumMoveResult result = await _handler.HandleAsync(plan);

        Assert.Equal(2, result.Moved);
        Assert.Equal("keep me", await File.ReadAllTextAsync(existing));
        Assert.Equal("first", await File.ReadAllTextAsync(
            Path.Combine(_destination, "same (2).jpg")));
        Assert.Equal("second", await File.ReadAllTextAsync(
            Path.Combine(_destination, "clip.mp4")));

        _db.ChangeTracker.Clear();
        Assert.Equal(@"Together\same (2).jpg",
            (await _db.Assets.SingleAsync(asset => asset.Id == photo)).RelativePath);
        Assert.Equal(@"Together\clip.mp4",
            (await _db.Assets.SingleAsync(asset => asset.Id == video)).RelativePath);
        Assert.Equal([photo, video], await _db.AlbumMembers
            .Where(member => member.AlbumId == albumId)
            .OrderBy(member => member.AssetId)
            .Select(member => member.AssetId)
            .ToListAsync());
        Assert.All(await _db.AlbumFileMoves.ToListAsync(),
            move => Assert.Equal(AlbumFileMoveState.Completed, move.State));
    }

    [Fact]
    public async Task Recover_UsesTheDestinationAsReceiptWhenTheProcessStoppedAfterFileMove()
    {
        int albumId = AddAlbum(AlbumOrigin.Made);
        int assetId = AddFile(albumId, Path.Combine("Old", "one.jpg"), "original");
        AlbumMovePlan plan = await _handler.PlanAsync(albumId, _destination);
        AlbumMovePlanItem item = Assert.Single(plan.Items);

        await _repository.BeginAsync(
            plan.OperationId,
            plan.AlbumId,
            [new AlbumMoveJournalPlan(
                item.AssetId,
                plan.PhotoSourceId,
                item.SourceRelativePath,
                item.DestinationRelativePath,
                item.ExpectedLength,
                item.ExpectedModifiedUtc)]);

        // The process could stop in precisely this gap: File.Move completed,
        // neither the journal state nor the asset path did.
        File.Move(item.SourceFullPath, item.DestinationFullPath);

        IReadOnlyList<AlbumMoveResult> recovered = await _handler.RecoverAsync();

        Assert.Equal(1, Assert.Single(recovered).Moved);
        _db.ChangeTracker.Clear();
        Assert.Equal(@"Together\one.jpg",
            (await _db.Assets.SingleAsync(asset => asset.Id == assetId)).RelativePath);
        Assert.Equal(AlbumFileMoveState.Completed,
            (await _db.AlbumFileMoves.SingleAsync()).State);
        Assert.Equal("original", await File.ReadAllTextAsync(item.DestinationFullPath));
    }

    [Fact]
    public async Task Plan_RefusesAnAlbumWhoseOriginalsSpanPhotoSources()
    {
        int albumId = AddAlbum(AlbumOrigin.Made);
        AddFile(albumId, Path.Combine("Old", "one.jpg"), "one");

        string secondRoot = Path.Combine(_root, "more-photos");
        Directory.CreateDirectory(secondRoot);
        _db.PhotoSources.Add(new PhotoSource
        {
            Id = 2,
            Path = secondRoot,
            AddedUtc = DateTime.UtcNow,
        });
        _db.SaveChanges();
        AddFile(albumId, "two.jpg", "two", sourceId: 2, sourceRoot: secondRoot);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.PlanAsync(albumId, _destination));

        Assert.Contains("more than one photo source", error.Message);
        Assert.Empty(await _db.AlbumFileMoves.ToListAsync());
    }

    [Fact]
    public async Task Plan_RequiresASuggestedAlbumToBeKeptFirst()
    {
        int albumId = AddAlbum(AlbumOrigin.Proposed);
        AddFile(albumId, Path.Combine("Old", "one.jpg"), "one");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.PlanAsync(albumId, _destination));

        Assert.Contains("Keep the suggested album", error.Message);
    }

    private int AddAlbum(AlbumOrigin origin)
    {
        var album = new Album
        {
            Name = "A day out",
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow,
            Kind = AlbumKind.Period,
            Origin = origin,
            ProposalKey = origin == AlbumOrigin.Proposed ? $"test-{Guid.NewGuid():N}"[..24] : null,
            BuiltUtc = DateTime.UtcNow,
        };

        _db.Albums.Add(album);
        _db.SaveChanges();
        return album.Id;
    }

    private int AddFile(
        int albumId,
        string relativePath,
        string contents,
        int sourceId = 1,
        string? sourceRoot = null)
    {
        string root = sourceRoot ?? _source;
        string fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        var info = new FileInfo(fullPath);

        var asset = new Asset
        {
            PhotoSourceId = sourceId,
            RelativePath = relativePath,
            Length = info.Length,
            ModifiedUtc = info.LastWriteTimeUtc,
            CreatedUtc = info.CreationTimeUtc,
            IndexedUtc = DateTime.UtcNow,
            Kind = Path.GetExtension(relativePath).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                ? AssetKind.Video
                : AssetKind.Photo,
            Status = AssetStatus.Ready,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        _db.AlbumMembers.Add(new AlbumMember
        {
            AlbumId = albumId,
            AssetId = asset.Id,
            AddedUtc = DateTime.UtcNow,
        });
        _db.SaveChanges();
        return asset.Id;
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}

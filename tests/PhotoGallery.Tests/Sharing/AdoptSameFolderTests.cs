using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A folder that has only just been added takes the identity another computer
/// uses for it - and only a folder with nothing filed under its own yet.
/// </summary>
public sealed class AdoptSameFolderTests : IDisposable
{
    private readonly TwoLibraries _house = new();

    private Library Dad => _house.Dad;

    private AdoptSameFolderHandler OnDad =>
        new(Dad.Index, new SqliteAssetRepository(Dad.Db), Dad.Writing);

    [Fact]
    public async Task AnEmptyFolder_TakesTheirIdentity()
    {
        Dad.Unpair();
        Guid theirs = _house.Mum.SharedSourceId;

        await OnDad.HandleAsync(Dad.SharedSourceId, theirs);

        Assert.Equal(theirs, OriginalSharedId());
    }

    [Fact]
    public async Task AFolderWithPhotos_IsRefusedAndKeepsItsOwn()
    {
        Dad.Unpair();
        Dad.Photo(@"2019\a.jpg");
        Guid own = Dad.SharedSourceId;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => OnDad.HandleAsync(own, _house.Mum.SharedSourceId));

        Assert.Equal(own, OriginalSharedId());
    }

    [Fact]
    public async Task AnIdentityAnotherFolderHereHas_IsRefused()
    {
        Dad.Unpair();
        Guid theirs = _house.Mum.SharedSourceId;
        Dad.Folder(@"\\192.168.50.103\PhotoGallery", theirs);
        PhotoGallery.Domain.Library.PhotoSource mapped = Dad.Folder(@"Z:\PhotoGallery");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => OnDad.HandleAsync(mapped.SharedId, theirs));
    }

    [Fact]
    public async Task AFolderThatIsNotThisLibrarys_IsRefused() =>
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => OnDad.HandleAsync(Guid.NewGuid(), _house.Mum.SharedSourceId));

    private Guid OriginalSharedId() =>
        Dad.Db.PhotoSources.AsNoTracking().Single(source => source.Id == 1).SharedId;

    public void Dispose() => _house.Dispose();
}

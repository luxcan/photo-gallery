using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Search;
using PhotoGallery.Infrastructure;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.App;

/// <summary>
/// A folder chosen after a typed description shows the whole folder.
/// </summary>
/// <remarks>
/// The bug as it was reported: a folder the tree counted at 252 opened onto an
/// empty wall, and closing and reopening the app put the photographs back.
/// Choosing a folder already let go of a person and a place, but not of a
/// description, whose answer is a few hundred photographs from the whole
/// library - so the folder showed only the ones that answer happened to hold.
///
/// <para>Against the real reader, because the wall going empty is the reader
/// intersecting the folder with the ranking. The encoder and the stored
/// vectors are stubbed: what is under test is which question the wall asks,
/// not how a phrase is ranked.</para>
/// </remarks>
public sealed class ChoosingAFolderAfterASearchTests : IDisposable
{
    /// <summary>The one photograph the description matches, in Beach.</summary>
    private const int Matched = 1;

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly GalleryViewModel _gallery;

    public ChoosingAFolderAfterASearchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-folder-after-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _services = new ServiceCollection()
            .AddPhotoGalleryInfrastructure(_root)
            .AddPhotoGalleryHandlers()
            .AddSingleton<IContentEncoder, OneAnswerEncoder>()
            .AddSingleton<IContentRepository, OneMatchContent>()
            .BuildServiceProvider();

        using (IServiceScope scope = _services.CreateScope())
        {
            GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
            db.Database.Migrate();
            db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });

            for (int id = 1; id <= 3; id++)
            {
                db.Assets.Add(Photo(id, $@"Beach\{id}.jpg"));
            }

            for (int id = 4; id <= 6; id++)
            {
                db.Assets.Add(Photo(id, $@"Home\{id}.jpg"));
            }

            db.SaveChanges();
        }

        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IThumbnailStore>());
        _gallery.SetColumns(4);
    }

    [Fact]
    public async Task AFolderChosenAfterADescription_ShowsEverythingInIt()
    {
        await SearchForAsync("sunset");
        Assert.True(_gallery.IsContentFiltered);
        Assert.Equal(1, _gallery.TotalCount);

        await ChooseFolderAsync("Home");

        Assert.Equal(3, _gallery.TotalCount);
        Assert.False(_gallery.IsContentFiltered);
    }

    /// <summary>
    /// The box lets go of the words too, so it does not go on saying "sunset"
    /// over a folder that is no longer narrowed to sunsets.
    /// </summary>
    [Fact]
    public async Task AFolderChosenAfterADescription_EmptiesTheSearchBox()
    {
        await SearchForAsync("sunset");

        await ChooseFolderAsync("Beach");

        Assert.Equal(string.Empty, _gallery.SearchText);
        Assert.Equal(string.Empty, _gallery.SearchNote);
        Assert.Equal(3, _gallery.TotalCount);
    }

    /// <summary>
    /// With no search on, choosing a folder leaves the box alone - somebody
    /// half way through typing a name has not asked for anything yet.
    /// </summary>
    [Fact]
    public async Task AFolderChosenWithNoSearchOn_LeavesTheBoxAlone()
    {
        _gallery.SearchText = "sun";

        await ChooseFolderAsync("Home");

        Assert.Equal("sun", _gallery.SearchText);
        Assert.Equal(3, _gallery.TotalCount);
    }

    private async Task SearchForAsync(string phrase)
    {
        _gallery.SearchText = phrase;
        await _gallery.AcceptSearchCommand.ExecuteAsync(null);
    }

    /// <summary>Chooses a folder the way the tree does: choose, then read.</summary>
    private async Task ChooseFolderAsync(string relative)
    {
        await _gallery.LoadFoldersAsync();

        static IEnumerable<FolderItem> All(IEnumerable<FolderItem> nodes) =>
            nodes.SelectMany(node => All(node.Children).Prepend(node));

        _gallery.SelectedFolder = All(_gallery.Folders).Single(node => node.RelativeFolder == relative);
        await _gallery.LoadAsync();
    }

    private static Asset Photo(int id, string relativePath)
    {
        var taken = new DateTime(2020, 3, 1).AddDays(id);
        return new Asset
        {
            Id = id,
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1,
            ModifiedUtc = taken,
            CreatedUtc = taken,
            IndexedUtc = taken,
            TakenUtc = taken,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            Width = 80,
            Height = 60,
        };
    }

    private static ContentEmbedding Towards(float first)
    {
        float[] values = new float[ContentEmbedding.Dimensions];
        values[0] = first;
        values[1] = MathF.Sqrt(Math.Max(0f, 1f - (first * first)));
        return new ContentEmbedding(values);
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder; SQLite's pooled handle may outlive the provider.
        }
    }

    private sealed class OneAnswerEncoder : IContentEncoder
    {
        public Task<ContentEmbedding?> DescribePictureAsync(
            string previewPath, CancellationToken token = default) =>
            Task.FromResult<ContentEmbedding?>(Towards(1f));

        public Task<ContentEmbedding?> DescribePhraseAsync(
            string phrase, CancellationToken token = default) =>
            Task.FromResult<ContentEmbedding?>(Towards(1f));
    }

    /// <summary>One described photograph, so the ranking holds it alone.</summary>
    private sealed class OneMatchContent : IContentRepository
    {
        public Task SaveAsync(
            IReadOnlyList<ContentScanUpdate> updates, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ContentVector>> GetVectorsAsync(
            int? personId = null, PlaceFilter? place = null, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<ContentVector>>([new ContentVector(Matched, Towards(1f))]);

        public Task<(int Described, int Total)> CountAsync(CancellationToken token = default) =>
            Task.FromResult((1, 6));
    }
}

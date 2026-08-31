using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Refresh;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Infrastructure;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.App;

/// <summary>
/// That every use case the window can reach can actually be built.
/// </summary>
/// <remarks>
/// A handler whose dependency was never registered compiles perfectly and fails
/// at the moment somebody presses the button - for the scan, after the crawl has
/// already run. Nothing in a suite of view-model tests sees that, because a view
/// model under test is handed its collaborators rather than asked to resolve
/// them.
///
/// <para>Resolved rather than merely counted. Asking the container for the
/// handler is what walks the whole graph beneath it, which is where a gap
/// actually is.</para>
/// </remarks>
public sealed class ContainerTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"pg-container-{Guid.NewGuid():N}");

    public ContainerTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void EveryHandlerTheWindowCanReachCanBeBuilt()
    {
        using ServiceProvider services = Services();
        using IServiceScope scope = services.CreateScope();

        foreach (Type handler in Handlers())
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(handler));
        }
    }

    [Fact]
    public void TheScanCanBeBuiltWithEverythingItNowDependsOn()
    {
        // Named on its own because it is the core action and the one that has
        // grown a dependency on sharing: a scan applies the answers that have
        // been waiting for their photographs, so the whole exchange has to be
        // resolvable before the button works at all.
        using ServiceProvider services = Services();
        using IServiceScope scope = services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RefreshLibraryHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ApplyHeldDecisionsHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDecisionReader>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDecisionRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDecisionExchange>());
    }

    [Fact]
    public void ADatabasePathWithConnectionStringPunctuationWorks()
    {
        string folder = Path.Combine(_folder, "library;mode=value");
        Directory.CreateDirectory(folder);

        using ServiceProvider services = new ServiceCollection()
            .AddPhotoGalleryInfrastructure(folder)
            .BuildServiceProvider();
        using IServiceScope scope = services.CreateScope();

        GalleryDbContext database =
            scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
        database.Database.Migrate();

        Assert.True(File.Exists(Path.Combine(folder, "index.db")));
    }

    /// <summary>
    /// Every handler registered, found rather than listed.
    /// </summary>
    /// <remarks>
    /// A hand-written list would be a list somebody has to remember to add to,
    /// which is the same kind of omission this test exists to catch.
    /// </remarks>
    private static IEnumerable<Type> Handlers() =>
        new ServiceCollection()
            .AddPhotoGalleryHandlers()
            .Select(service => service.ServiceType);

    private ServiceProvider Services() =>
        new ServiceCollection()
            .AddPhotoGalleryInfrastructure(_folder)
            .AddPhotoGalleryHandlers()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
            });

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

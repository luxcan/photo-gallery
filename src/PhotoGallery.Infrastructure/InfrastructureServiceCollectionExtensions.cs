using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Faces;
using PhotoGallery.Infrastructure.Imaging;
using PhotoGallery.Infrastructure.Models;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Places;
using PhotoGallery.Infrastructure.Search;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Infrastructure;

/// <summary>
/// Binds the Application layer's ports to their concrete implementations.
/// </summary>
/// <remarks>
/// Called once a working folder has been chosen, because the connection string
/// is only knowable then - which is why the app builds its container after the
/// welcome screen rather than at startup.
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPhotoGalleryInfrastructure(
        this IServiceCollection services,
        string workingFolderRoot)
    {
        ArgumentNullException.ThrowIfNull(services);

        var workingFolder = new WorkingFolder(workingFolderRoot);
        services.AddSingleton<IWorkingFolder>(workingFolder);
        services.AddSingleton<IAppConfigStore, JsonAppConfigStore>();
        services.AddSingleton<IActivityLog, FileActivityLog>();

        services.AddDbContext<GalleryDbContext>(options =>
            options.UseSqlite($"Data Source={workingFolder.DatabasePath}"));

        services.AddScoped<ILibraryIndex, SqliteLibraryIndex>();
        services.AddScoped<IAssetRepository, SqliteAssetRepository>();
        services.AddScoped<IDuplicateRepository, SqliteDuplicateRepository>();
        services.AddSingleton<IQuarantineStore, FileSystemQuarantine>();
        services.AddSingleton<IMediaFileWalker, MediaFileWalker>();
        services.AddSingleton<IThumbnailStore, FileSystemThumbnailStore>();
        services.AddSingleton<IThumbnailGenerator, WindowsThumbnailGenerator>();
        services.AddSingleton<IKeyframeExtractor, ShellThumbnailKeyframeExtractor>();
        services.AddSingleton<IRenditionTurner, WindowsRenditionTurner>();
        services.AddSingleton<IOriginalOrientation, ExifOriginalOrientation>();
        services.AddSingleton<IOriginalFile, WindowsOriginalFile>();
        services.AddSingleton<IOriginalCoordinates, ExifOriginalCoordinates>();
        services.AddSingleton<ISourceAvailability, FileSystemSourceAvailability>();

        // Scoped, unlike the encoders below. Reading the places data costs about
        // 450 ms and holds some 50 MB of arrays; a refresh pays that once and
        // gives the memory back when it ends. The content encoder is a singleton
        // because loading it is 1.2 GB - a different bargain entirely.
        services.AddScoped<IGeocoder, GeoNamesGazetteer>();
        services.AddScoped<IPlaceRepository, SqlitePlaceRepository>();
        services.AddScoped<IPlaceReader, SqlitePlaceReader>();
        services.AddScoped<IGalleryReader, SqliteGalleryReader>();
        services.AddScoped<IFaceRepository, SqliteFaceRepository>();
        services.AddScoped<IPeopleReader, SqlitePeopleReader>();
        services.AddScoped<IPeopleRepository, SqlitePeopleRepository>();
        services.AddScoped<IContentRepository, SqliteContentRepository>();

        services.AddSingleton(ModelManifest.Default);
        services.AddSingleton<IModelFolder, ModelFolder>();
        services.AddSingleton<IModelStore, FileModelStore>();
        services.AddSingleton<IFaceScanner, OnnxFaceScanner>();

        // A singleton for the same reason the face scanner is one: the visual
        // graph alone is 1.2 GB to load, and the sessions are safe to run from
        // several threads at once.
        services.AddSingleton<IContentEncoder, ClipContentEncoder>();

        return services;
    }
}

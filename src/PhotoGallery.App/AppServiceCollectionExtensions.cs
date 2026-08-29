using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.Application.UseCases.Collections;
using PhotoGallery.Application.UseCases.Duplicates;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Application.UseCases.Models;
using PhotoGallery.Application.UseCases.OpenLibrary;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Application.UseCases.Preferences;
using PhotoGallery.Application.UseCases.Refresh;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Search;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Application.UseCases.Thumbnails;
using PhotoGallery.Application.UseCases.Videos;

namespace PhotoGallery.App;

/// <summary>
/// Every use case the window can reach, registered in one place.
/// </summary>
/// <remarks>
/// Lifted out of <c>App.OnStartup</c> so that it can be built without starting
/// the app. A handler whose dependency is not registered compiles perfectly and
/// fails at the moment somebody presses the button, which for the scan means
/// after the crawl has already run - and nothing in a suite of view-model tests
/// would ever see it.
/// </remarks>
public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddPhotoGalleryHandlers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddScoped<OpenLibraryHandler>()
            .AddScoped<AddPhotoSourceHandler>()
            .AddScoped<RemovePhotoSourceHandler>()
            .AddScoped<ScanPhotoSourceHandler>()
            .AddScoped<BuildThumbnailsHandler>()
            .AddScoped<BuildVideoKeyframesHandler>()
            .AddScoped<DetectFacesHandler>()
            .AddScoped<BuildCollectionsHandler>()
            .AddScoped<IndexContentHandler>()
            .AddScoped<LocatePhotosHandler>()
            .AddScoped<FindPlacesHandler>()
            .AddScoped<SearchPhotosHandler>()
            .AddScoped<GetPeopleBoardHandler>()
            .AddScoped<FindPeopleHandler>()
            .AddScoped<TurnPhotoHandler>()
            .AddScoped<RemovePhotoHandler>()
            .AddScoped<FindDuplicatesHandler>()
            .AddScoped<GetDuplicatesHandler>()
            .AddScoped<GetPersonReviewHandler>()
            .AddScoped<ProposeFacesHandler>()
            .AddScoped<AssignFacesHandler>()
            .AddScoped<RecheckPeopleHandler>()
            .AddScoped<MergedTurns>()
            .AddScoped<ApplyHeldDecisionsHandler>()
            .AddScoped<PublishDecisionsHandler>()
            .AddScoped<MergeDecisionsHandler>()
            .AddScoped<SetSharedFolderHandler>()
            .AddScoped<RefreshLibraryHandler>()
            .AddScoped<QueryGalleryHandler>()
            .AddScoped<GetFolderTreeHandler>()
            .AddScoped<SaveThemeHandler>()
            .AddScoped<SaveGalleryCellSizeHandler>()
            .AddScoped<SaveGallerySortOrderHandler>()
            .AddScoped<SaveNavigationCollapsedHandler>()
            .AddScoped<GetModelStatusHandler>()
            .AddScoped<ImportModelsHandler>();
    }
}

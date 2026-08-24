namespace PhotoGallery.Application.UseCases.Thumbnails;

/// <summary>What a thumbnail pass produced.</summary>
public sealed record ThumbnailBuildResult(
    int Pending, int Built, int Failed, TimeSpan Elapsed, bool WasCancelled)
{
    public string Summary => Pending == 0
        ? "every picture is already prepared"
        : (WasCancelled ? "stopped, and will resume where it left off: " : string.Empty)
        + $"{Built:N0} prepared, {Failed:N0} could not be read "
        + $"({Elapsed.TotalSeconds:N0}s)";
}

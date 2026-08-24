using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Models;

/// <summary>What pointing the app at a folder of model files achieved.</summary>
/// <param name="Rejected">
/// Files that were the right size and turned out not to be the right file. Named
/// because the alternative reads as the app having ignored them.
/// </param>
public sealed record ImportModelsResult(
    IReadOnlyList<FeatureStatus> Features,
    int Installed,
    IReadOnlyList<string> Rejected)
{
    /// <summary>Nothing in that folder was anything this app knows about.</summary>
    public bool FoundNothing => Installed == 0 && Rejected.Count == 0;

    /// <summary>Features that can run now and could not before this ran.</summary>
    public IReadOnlyList<ModelFeature> NowReady =>
        [.. Features.Where(feature => feature.IsReady).Select(feature => feature.Feature)];

    public string Summary
    {
        get
        {
            if (FoundNothing)
            {
                return "Nothing in that folder is a model this app uses. "
                       + "Check you chose the folder the files were downloaded into.";
            }

            List<string> parts = [];

            if (Installed > 0)
            {
                parts.Add(Installed == 1
                    ? "1 file installed"
                    : $"{Installed:N0} files installed");
            }

            if (Rejected.Count > 0)
            {
                // Named individually: with at most six files in play, which one
                // is wrong is the whole of what the user needs to know.
                parts.Add(Rejected.Count == 1
                    ? $"{Rejected[0]} is the right size but not the right file"
                    : $"{string.Join(", ", Rejected)} are the right sizes but not the right files");
            }

            return string.Join(", and ", parts) + ".";
        }
    }
}

using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Models;

/// <summary>
/// Whether one feature's models are on disk, and what is still wanted.
/// </summary>
/// <remarks>
/// Three states matter to the user and not two. Nothing installed is a feature
/// they have not set up yet; everything installed is one that works; some of it
/// installed is a mistake in progress - four files were copied and one was
/// missed - and saying "not installed" to that would send them back to the
/// beginning of a job they had nearly finished.
/// </remarks>
public sealed record FeatureStatus(ModelFeature Feature, IReadOnlyList<ModelFileStatus> Files)
{
    /// <summary>Every file is present and is the one the manifest describes.</summary>
    public bool IsReady => Files.Count > 0 && Files.All(file => file.State == ModelState.Ready);

    /// <summary>Nothing has been installed for this feature at all.</summary>
    public bool IsMissing => Files.All(file => file.State == ModelState.Missing);

    /// <summary>Some of it arrived and some did not.</summary>
    public bool IsPartial => !IsReady && !IsMissing;

    /// <summary>What is still to come, for naming the remaining work.</summary>
    public IReadOnlyList<ModelFileStatus> Outstanding =>
        [.. Files.Where(file => file.State != ModelState.Ready)];

    /// <summary>
    /// A file that was there and was not what it claimed to be.
    /// </summary>
    /// <remarks>
    /// Worth separating from simply missing: the store deletes it, so the user
    /// is looking at a folder they believe they filled, and "not installed"
    /// alone would read as the app having ignored them.
    /// </remarks>
    public bool WasDamaged => Files.Any(file => file.State == ModelState.Damaged);

    /// <summary>How much disk the whole feature costs.</summary>
    public long Bytes => Files.Sum(file => file.Bytes);

    /// <summary>How much of it is still to be found.</summary>
    public long OutstandingBytes => Outstanding.Sum(file => file.Bytes);

    /// <summary>The terms attached to these files, each said once.</summary>
    public IReadOnlyList<string> Licences =>
        [.. Files.Select(file => file.Licence).Distinct(StringComparer.Ordinal)];
}

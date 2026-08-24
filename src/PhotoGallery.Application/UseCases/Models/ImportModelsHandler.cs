using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Models;

/// <summary>
/// Takes whatever model files a folder holds and installs the ones this app uses.
/// </summary>
/// <remarks>
/// Files are matched by <em>size</em> and then proved by digest, not matched by
/// name. The names in the manifest are this app's own; upstream ships the two
/// content graphs as <c>visual/model.onnx</c> and <c>textual/model.onnx</c>, and
/// the face pack as an archive of eight. Matching on name would have made
/// "rename these six files first" a step in the instructions, and a step like
/// that is where people give up.
///
/// <para>Sizes carry the search because they are distinctive to the byte and a
/// directory listing yields them for nothing, so at most one 1.2 GB read is
/// spent on a candidate rather than one per file in the folder. The digest still
/// decides: a same-sized impostor is rejected exactly as before.</para>
/// </remarks>
public sealed class ImportModelsHandler
{
    /// <summary>
    /// How far below the chosen folder to look.
    /// </summary>
    /// <remarks>
    /// One level, which is what the content export needs and what a folder of
    /// unzipped archives looks like. Not a full walk: the user is choosing a
    /// folder in a dialog and could as easily choose a drive.
    /// </remarks>
    private const int Depth = 1;

    private readonly IModelStore _models;
    private readonly GetModelStatusHandler _status;

    public ImportModelsHandler(IModelStore models, GetModelStatusHandler status)
    {
        _models = models;
        _status = status;
    }

    public async Task<ImportModelsResult> HandleAsync(
        string folder,
        IProgress<string>? naming = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        ILookup<long, string> bySize = CandidatesBySize(folder);
        int installed = 0;
        List<string> rejected = [];

        foreach (ModelId id in FeatureModels.All)
        {
            if (_models.StateOf(id) == ModelState.Ready)
            {
                continue;
            }

            ModelDescriptor descriptor = _models.Describe(id);

            foreach (string candidate in bySize[descriptor.Bytes])
            {
                cancellationToken.ThrowIfCancellationRequested();
                naming?.Report(Path.GetFileName(candidate));

                ModelState state = await _models
                    .ImportAsync(id, candidate, cancellationToken)
                    .ConfigureAwait(false);

                if (state == ModelState.Ready)
                {
                    installed++;
                    break;
                }

                // Right size, wrong file. Worth saying out loud, because the user
                // is looking at a folder they believe holds the model.
                rejected.Add(Path.GetFileName(candidate));
            }
        }

        return new ImportModelsResult(_status.Handle(), installed, rejected);
    }

    /// <summary>
    /// Every file at or just below the folder, grouped by length.
    /// </summary>
    /// <remarks>
    /// Unreadable folders are skipped rather than thrown from: a download folder
    /// with one locked subdirectory in it should still yield the models beside
    /// it.
    /// </remarks>
    private static ILookup<long, string> CandidatesBySize(string folder)
    {
        List<FileInfo> found = [];
        Collect(new DirectoryInfo(folder), Depth, found);

        return found.ToLookup(file => file.Length, file => file.FullName);
    }

    private static void Collect(DirectoryInfo directory, int depth, List<FileInfo> found)
    {
        try
        {
            found.AddRange(directory.EnumerateFiles());

            if (depth > 0)
            {
                foreach (DirectoryInfo child in directory.EnumerateDirectories())
                {
                    Collect(child, depth - 1, found);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

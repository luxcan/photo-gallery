using System.Security.Cryptography;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Models;

/// <inheritdoc cref="IModelStore"/>
public sealed class FileModelStore : IModelStore
{
    /// <summary>
    /// The name a copy carries until its digest has been checked.
    /// </summary>
    /// <remarks>
    /// The app only ever looks for the manifest's file name, so a copy that was
    /// interrupted or turned out to be the wrong file is invisible to it. That
    /// one rule is what makes "imported once" true without needing to record
    /// anything about attempts that failed.
    /// </remarks>
    private const string PartialSuffix = ".partial";

    private readonly IModelFolder _folder;
    private readonly ModelManifest _manifest;

    /// <summary>
    /// The last answer given for a model, against the file it was given about.
    /// </summary>
    /// <remarks>
    /// Verifying a model means reading all 166 MB of it and digesting it, and
    /// asking is cheap enough to look free: the face pass alone asks four times
    /// before it opens a single preview, once for each model in the handler and
    /// again as each session is built. That was three hundred megabytes of
    /// reading to answer a question nothing had changed the answer to.
    ///
    /// <para>Keyed on the file's length and last-write time, so a model replaced
    /// or damaged on disk is digested afresh - the guarantee that no unverified
    /// path is ever handed out is not weakened, only stopped from being paid for
    /// repeatedly.</para>
    /// </remarks>
    private readonly Dictionary<ModelId, (long Length, DateTime WrittenUtc, ModelState State)>
        _verified = [];

    private readonly Lock _gate = new();

    public FileModelStore(IModelFolder folder, ModelManifest manifest)
    {
        _folder = folder;
        _manifest = manifest;
    }

    public ModelDescriptor Describe(ModelId id) => _manifest.For(id);

    public string ResolvePath(ModelId id) =>
        Path.Combine(_folder.Path, _manifest.For(id).FileName);

    public ModelState StateOf(ModelId id)
    {
        ModelDescriptor descriptor = _manifest.For(id);
        string path = ResolvePath(id);

        FileInfo file = new(path);
        if (!file.Exists)
        {
            Forget(id);
            return ModelState.Missing;
        }

        (long Length, DateTime WrittenUtc) identity = (file.Length, file.LastWriteTimeUtc);

        lock (_gate)
        {
            if (_verified.TryGetValue(id, out var last)
                && (last.Length, last.WrittenUtc) == identity)
            {
                return last.State;
            }
        }

        ModelState state = Matches(path, descriptor)
            ? ModelState.Ready
            : ModelState.Damaged;

        if (state == ModelState.Damaged)
        {
            // Removed rather than reported and left: a file already known not to
            // be the model would otherwise be read and digested again on every
            // start, and would keep looking like something that might yet work.
            Delete(path);
            Forget(id);
            return ModelState.Damaged;
        }

        lock (_gate)
        {
            _verified[id] = (identity.Length, identity.WrittenUtc, state);
        }

        return state;
    }

    public async Task<ModelState> ImportAsync(
        ModelId id,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        ModelDescriptor descriptor = _manifest.For(id);
        string destination = ResolvePath(id);
        string partial = destination + PartialSuffix;

        Directory.CreateDirectory(_folder.Path);

        // Whatever a previous attempt left is worthless: there is no resume here,
        // and starting over on a local copy costs seconds.
        Delete(partial);

        try
        {
            await CopyAsync(sourcePath, partial, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            Delete(partial);
            return StateOf(id);
        }

        if (!Matches(partial, descriptor))
        {
            Delete(partial);
            return ModelState.Damaged;
        }

        File.Move(partial, destination, overwrite: true);

        // The file at this path is a different file now, so whatever was
        // concluded about the last one says nothing about it.
        Forget(id);
        return StateOf(id);
    }

    private void Forget(ModelId id)
    {
        lock (_gate)
        {
            _verified.Remove(id);
        }
    }

    /// <summary>
    /// Whether a file is byte-for-byte the one the descriptor names.
    /// </summary>
    /// <remarks>
    /// Length first, because it rejects a truncated download without reading
    /// 166 MB to reach the same conclusion.
    /// </remarks>
    private static bool Matches(string path, ModelDescriptor descriptor)
    {
        try
        {
            if (new FileInfo(path).Length != descriptor.Bytes)
            {
                return false;
            }

            using FileStream stream = File.OpenRead(path);
            return string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(stream)),
                descriptor.Sha256,
                StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read is indistinguishable from the wrong one,
            // and both mean the same thing: do not use it.
            return false;
        }
    }

    private static async Task CopyAsync(
        string source, string destination, CancellationToken cancellationToken)
    {
        await using FileStream input =
            new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using FileStream output =
            new(destination, FileMode.Create, FileAccess.Write, FileShare.None);

        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Something else is holding it. The next call asks the disk again.
        }
    }
}

using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Models;

/// <inheritdoc cref="IModelFolder"/>
/// <remarks>
/// Read once and held, rather than asked of the config file on every path
/// resolution: a model path is asked for several times per pass, and the answer
/// only changes when somebody changes it here.
/// </remarks>
public sealed class ModelFolder : IModelFolder
{
    private readonly IWorkingFolder _workingFolder;
    private readonly IAppConfigStore _config;
    private readonly Lock _gate = new();

    private string? _chosen;
    private bool _read;

    public ModelFolder(IWorkingFolder workingFolder, IAppConfigStore config)
    {
        _workingFolder = workingFolder;
        _config = config;
    }

    public string Default => _workingFolder.ModelsPath;

    public bool WasChosen => !string.IsNullOrWhiteSpace(Chosen);

    public string Path => WasChosen ? Chosen! : Default;

    public void Use(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        lock (_gate)
        {
            // Read before written, so a save cannot drop the library path or the
            // diagnostics flag that share this file.
            _config.Save(_config.Load() with { ModelsFolder = folder });
            _chosen = folder;
            _read = true;
        }
    }

    private string? Chosen
    {
        get
        {
            lock (_gate)
            {
                if (!_read)
                {
                    _chosen = _config.Load().ModelsFolder;
                    _read = true;
                }

                return _chosen;
            }
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Storage;

/// <summary>
/// Keeps <see cref="AppConfig"/> in <c>config.json</c> beside the executable.
/// </summary>
/// <remarks>
/// Beside the exe and nowhere else. This ships as one self-contained file, so a
/// config that travels with it means the app can be moved, copied to a USB stick
/// or run from a share without losing where its library is - and deleting that
/// one file gives a genuinely clean start, which is only true if there is no
/// second copy anywhere for it to fall back to.
/// </remarks>
public sealed class JsonAppConfigStore : IAppConfigStore
{
    private const string FileName = "config.json";

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly Lock _gate = new();

    public JsonAppConfigStore(string? filePath = null) =>
        _filePath = filePath ?? DefaultFilePath();

    public string FilePath => _filePath;

    /// <summary>
    /// <c>config.json</c> in the folder the executable is running from.
    /// </summary>
    private static string DefaultFilePath()
    {
        string? folder = Path.GetDirectoryName(Environment.ProcessPath);

        // AppContext.BaseDirectory is the fallback only because ProcessPath can
        // be null when hosted rather than launched - it is the same folder.
        return Path.Combine(
            string.IsNullOrEmpty(folder) ? AppContext.BaseDirectory : folder,
            FileName);
    }

    public AppConfig Load()
    {
        lock (_gate)
        {
            return LoadUnlocked();
        }
    }

    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_gate)
        {
            SaveUnlocked(config);
        }
    }

    public void ForgetLastFolder()
    {
        lock (_gate)
        {
            SaveUnlocked(LoadUnlocked() with { LastWorkingFolder = null });
        }
    }

    public void RememberFolder(string workingFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingFolderPath);
        string full = Path.GetFullPath(workingFolderPath).TrimEnd('\\', '/');

        lock (_gate)
        {
            SaveUnlocked(LoadUnlocked() with { LastWorkingFolder = full });
        }
    }

    private AppConfig LoadUnlocked()
    {
        if (!File.Exists(_filePath))
        {
            return AppConfig.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_filePath), s_json)
                ?? AppConfig.Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException
                                       or UnauthorizedAccessException)
        {
            // A corrupt or locked config must never stop the app starting.
            return AppConfig.Empty;
        }
    }

    private void SaveUnlocked(AppConfig config)
    {
        try
        {
            // Written to a temporary file and moved into place, so a crash
            // mid-write cannot leave a half-written config behind.
            string temporary = _filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(config, s_json));
            File.Move(temporary, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is not worth failing the operation that
            // prompted it.
        }
    }
}

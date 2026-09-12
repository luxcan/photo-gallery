using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Storage;

/// <summary>
/// Keeps <see cref="AppConfig"/> in <c>config.json</c> at a fixed per-user
/// address, <c>%LOCALAPPDATA%\PhotoGallery</c>.
/// </summary>
/// <remarks>
/// It used to live beside the executable, so that the app could be moved, copied
/// to a USB stick or run from a share without losing where its library is. That
/// reasoning was sound and the address was still wrong, because publishing
/// EMPTIES the install folder before it writes to it: the deploy script had to
/// save the file and put it back in a <c>finally</c>, and a publish that failed
/// part way - the app still running and holding its own exe was enough - took the
/// remembered library and the models folder with it. A pointer kept in the one
/// place the deployment destroys is a pointer that has to be rescued by hand.
///
/// <para>Per-user rather than beside the binary also means the answer is the same
/// whichever copy of the app is running, which is what an installed application
/// on Windows is expected to do.</para>
///
/// <para><b>Adopted once, never fallen back on.</b> A config left beside the
/// executable by an older build is moved here the first time this runs and then
/// deleted, so there is exactly one copy afterwards. That is deliberately not the
/// same thing as reading the old place whenever the new one is missing: deleting
/// <c>config.json</c> has to mean a clean start, and it only does if nothing
/// anywhere can quietly put it back. Being a move rather than a copy is what
/// makes "once" true without a marker file to remember it by.</para>
///
/// <para>Set <c>PHOTOGALLERY_CONFIG_DIR</c> to point a run somewhere else. That
/// is how a debug run is aimed at a throwaway library instead of the real one -
/// which used to happen by accident, because a build in <c>bin\Debug</c> had its
/// own file beside its own exe and now shares the installed app's address.</para>
/// </remarks>
public sealed class JsonAppConfigStore : IAppConfigStore
{
    private const string FileName = "config.json";

    /// <summary>Environment variable that moves the config for one run.</summary>
    public const string DirectoryVariable = "PHOTOGALLERY_CONFIG_DIR";

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly Lock _gate = new();

    /// <param name="filePath">
    /// Exactly where to keep it. A path given here is an instruction, so nothing
    /// is adopted from anywhere else; this is what tests use.
    /// </param>
    /// <param name="adoptFrom">
    /// An older config to take over and remove, when this is the first run at the
    /// new address. Defaults to the file beside the executable, and only when
    /// neither <paramref name="filePath"/> nor the environment has named a
    /// location of its own.
    /// </param>
    public JsonAppConfigStore(string? filePath = null, string? adoptFrom = null)
    {
        _filePath = filePath ?? DefaultFilePath();

        string? older = adoptFrom ?? (filePath is null && Overridden() is null
            ? Path.Combine(ExecutableDirectory(), FileName)
            : null);

        if (older is not null)
        {
            Adopt(older);
        }
    }

    public string FilePath => _filePath;

    /// <summary>Where the config lives when nothing has been asked for.</summary>
    public static string DefaultFilePath() => Path.Combine(DirectoryFor(Overridden()), FileName);

    /// <summary>
    /// The folder for a given value of <see cref="DirectoryVariable"/> - the
    /// per-user one when it is unset or blank. Split out from reading the
    /// environment so the rule can be tested without setting a variable on the
    /// whole test process.
    /// </summary>
    public static string DirectoryFor(string? overrideValue) =>
        string.IsNullOrWhiteSpace(overrideValue) ? PerUserDirectory() : overrideValue;

    /// <summary><c>%LOCALAPPDATA%\PhotoGallery</c>.</summary>
    public static string PerUserDirectory()
    {
        // DoNotVerify returns the path even when the folder has never been
        // created, which is precisely the first-run case.
        string local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        // Beside the executable is the last resort rather than an exception: a
        // config is a convenience, and failing to resolve a well-known folder
        // must not be the reason the app will not start.
        return string.IsNullOrEmpty(local)
            ? ExecutableDirectory()
            : Path.Combine(local, "PhotoGallery");
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

    private static string? Overridden()
    {
        string? set = Environment.GetEnvironmentVariable(DirectoryVariable);
        return string.IsNullOrWhiteSpace(set) ? null : set;
    }

    /// <summary>The folder the running executable is in.</summary>
    private static string ExecutableDirectory()
    {
        string? folder = Path.GetDirectoryName(Environment.ProcessPath);

        // AppContext.BaseDirectory is the fallback only because ProcessPath can
        // be null when hosted rather than launched - it is the same folder.
        return string.IsNullOrEmpty(folder) ? AppContext.BaseDirectory : folder;
    }

    /// <summary>
    /// Takes over a config from an older address, once. Does nothing if there is
    /// already one here - what is at the new address is always the answer, and an
    /// older file left beside it is never read again.
    /// </summary>
    private void Adopt(string older)
    {
        if (string.Equals(older, _filePath, StringComparison.OrdinalIgnoreCase)
            || File.Exists(_filePath)
            || !File.Exists(older))
        {
            return;
        }

        try
        {
            EnsureFolder();
            File.Copy(older, _filePath);

            // Copy then delete rather than Move, so a failure to remove the old
            // one still leaves a readable config at the new address. The old file
            // is then adopted again on a later start only if somebody has since
            // deleted the new one, which is rare enough to prefer over a move
            // that can lose the file outright.
            File.Delete(older);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An install that cannot be tidied still has to start.
        }
    }

    private void EnsureFolder()
    {
        string? folder = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
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
            EnsureFolder();

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

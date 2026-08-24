using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Storage;

/// <inheritdoc cref="IActivityLog"/>
/// <remarks>
/// One file per day under the working folder's <c>logs\</c>, which has existed
/// since the folder layout was written and until now had nothing to put in it.
///
/// <para>Opened and closed per line rather than held: the same library can be
/// open in two copies of the app, and a held handle would make the second one
/// fail at the first line it tried to write.</para>
///
/// <para>Every failure is swallowed. A log that cannot write must not become the
/// thing that breaks the app it was meant to explain.</para>
/// </remarks>
public sealed class FileActivityLog : IActivityLog
{
    private readonly IWorkingFolder _workingFolder;
    private readonly object _gate = new();

    public FileActivityLog(IWorkingFolder workingFolder) => _workingFolder = workingFolder;

    public void Append(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        try
        {
            Directory.CreateDirectory(_workingFolder.LogsPath);
            string path = Path.Combine(
                _workingFolder.LogsPath, $"activity-{DateTime.Now:yyyyMMdd}.log");

            lock (_gate)
            {
                File.AppendAllText(
                    path, $"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            // Nowhere left to report this, and it must not propagate.
        }
    }
}

using System.Diagnostics;
using Serilog;
using Serilog.Events;

namespace PhotoGallery.App.Shell;

/// <summary>
/// A detailed record of what the app is doing, written only when it has been
/// switched on.
/// </summary>
/// <remarks>
/// Separate from the activity log, which is for the user and says what the app
/// did - scanned a folder, prepared pictures. This is for working out why it did
/// not: exceptions with their stacks, the binding failures WPF otherwise
/// swallows, and the sequence leading up to whatever went wrong.
///
/// <para>Overwritten on every start. A log that grows is a log nobody reads, and
/// the question being asked is always "what happened just now" - so the file is
/// exactly one run, and the one before it is kept beside it in case the app did
/// not survive long enough to be asked.</para>
/// </remarks>
public static class DiagnosticLog
{
    public const string FileName = "diagnostic.log";

    private const string PreviousFileName = "diagnostic.previous.log";

    private static bool s_started;

    public static bool IsOn => s_started;

    public static string? Path { get; private set; }

    /// <summary>
    /// Begins a fresh log in the given folder, keeping the last run's beside it.
    /// </summary>
    public static void Start(string logsFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsFolder);

        if (s_started)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(logsFolder);

            string current = System.IO.Path.Combine(logsFolder, FileName);
            string previous = System.IO.Path.Combine(logsFolder, PreviousFileName);

            if (File.Exists(current))
            {
                File.Move(current, previous, overwrite: true);
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    current,
                    outputTemplate:
                        "{Timestamp:HH:mm:ss.fff} {Level:u3} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Path = current;
            s_started = true;

            // WPF reports a failed binding to a trace source and nowhere else, so
            // a screen that silently shows the wrong thing leaves no trace at
            // all. Routed here, those become the first thing to look at.
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingListener());
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

            Log.Information("diagnostic log started");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException)
        {
            // A log that cannot be written must not stop the app it was meant to
            // help.
            s_started = false;
            Path = null;
        }
    }

    public static void Write(string message)
    {
        if (s_started)
        {
            Log.Information("{Message}", message);
        }
    }

    public static void Write(string message, Exception error)
    {
        if (s_started)
        {
            Log.Error(error, "{Message}", message);
        }
    }

    public static void Stop()
    {
        if (!s_started)
        {
            return;
        }

        Log.Information("closing");
        Log.CloseAndFlush();
        s_started = false;
    }

    /// <summary>Passes WPF's binding complaints into the log.</summary>
    private sealed class BindingListener : TraceListener
    {
        /// <summary>
        /// What a binding on a container the list has taken back looks like.
        /// </summary>
        /// <remarks>
        /// A virtualised list recycles its containers, and a container being
        /// recycled is briefly detached from the tree while its bindings are
        /// still live - so each one complains that it cannot find its source.
        /// Nothing is wrong: the row it belonged to has scrolled away.
        ///
        /// <para>Measured on this library, scrolling the grid produced several
        /// hundred of these in a single millisecond, each one a synchronous
        /// write to disk on the dispatcher - so this is not tidiness. Left in,
        /// switching the log on was enough to make the grid stutter, and the
        /// lines that say what the app was actually doing were buried among
        /// thousands that say nothing.</para>
        ///
        /// <para>A null item is what makes this safe to drop. A binding with a
        /// real object behind it names that object here, so a genuine mistake -
        /// a property that does not exist, a name spelt wrong - still comes
        /// through.</para>
        /// </remarks>
        private const string Detached = "DataItem=null";

        public override void Write(string? message)
        {
            // Bindings arrive in fragments; only complete lines are worth a row.
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)
                && !message.Contains(Detached, StringComparison.Ordinal))
            {
                Log.Write(LogEventLevel.Warning, "binding: {Message}", message);
            }
        }
    }
}
